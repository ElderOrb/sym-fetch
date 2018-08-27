using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace sym_fetch
{
	public enum OutputStyle
	{
		Debugger,
		SideBySide
	}

	public enum DownloadResult
	{
		NotFound,
		Success,
		Exists,
		Error
	}

	public sealed class Downloader
	{
		public Downloader(
			string directory = null,
			OutputStyle style = OutputStyle.SideBySide,
			string server = "https://msdl.microsoft.com/download/symbols",
			int bufferSize = 4096)
		{
			this.style = style;
			this.server = server;
			this.directory = directory;
			this.bufferSize = bufferSize;
		}

		public struct Assembly
		{
			public string Path;
			public string Name;
			public string PdbGuid;
			public bool IsCompressed;

			public Assembly(String path)
			{
				this.Path = path;
				var segments = path.Split("/"[0]);

				this.Name = segments[segments.Length - 1];
				this.PdbGuid = segments[segments.Length - 2];
				this.IsCompressed = false;
			}

			public void SetPath(string path)
			{
				this.Path = path;
			}

			public void SetName()
			{
				this.Name = this.Path.Split("/"[0])[this.Path.Split("/"[0]).Length - 1];
			}

			public void SetNameUsingPath(string path)
			{
				this.Name = path;
			}
		}

		private const Int32 default_decimals = 2;

		private const string userAgent = @"Microsoft-Symbol-Server/10.0.10522.521";

		private readonly OutputStyle style;
		private readonly string server;
		private readonly string directory;

		private readonly Int32 bufferSize = 4096;

		public readonly Dictionary<string, string> FailedFiles = new Dictionary<string, string>();

		private HttpWebResponse Retry(Assembly asm, bool headVerb)
		{
			var path = ProbeWithUnderscore(asm.Path);
			var req = (HttpWebRequest)System.Net.WebRequest.Create(path);
			req.UserAgent = userAgent;

			if (headVerb)
			{
				req.Method = "HEAD";
			}

			return GetResponseNoException(req);
		}

		private HttpWebResponse RetryFilePointer(Assembly asm)
		{
			var path = ProbeWithFilePointer(asm.Path);
			var req = (HttpWebRequest)System.Net.WebRequest.Create(path);
			req.UserAgent = userAgent;

			return GetResponseNoException(req);
		}

		// todo: clean this up
		private long ProcessFileSize(HttpWebResponse webResp, out string filePath)
		{
			long length = 0;
			filePath = null;
			Stream receiveStream = webResp.GetResponseStream();
			Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
			StreamReader readStream = new StreamReader(receiveStream, encode);
			Char[] read = new Char[webResp.ContentLength];
			readStream.Read(read, 0, (int)webResp.ContentLength);

			string file = new string(read, 0, (int)webResp.ContentLength);

			if (file.Contains("PATH"))
			{
				file = file.Substring(5, file.Length - 5); //Removing PATH: from the output

				try
				{
					FileInfo fInfo = new FileInfo(file);
					if (fInfo.Exists)
					{
						length = fInfo.Length;
						filePath = file;
					}
				}
				catch (Exception ex)
				{
					WriteToLog(file, ex);
				}
			}
			else
			{
				int position = webResp.ResponseUri.PathAndQuery.IndexOf(".pdb");
				string fileName = webResp.ResponseUri.PathAndQuery.Substring(1, position + 3);
				if (!FailedFiles.ContainsKey(fileName))
					FailedFiles.Add(fileName, " - No matching PDBs found - " + file);
			}

			return length;
		}

		private static string ProbeWithUnderscore(string path)
		{
			path = path.Remove(path.Length - 1);
			return path.Insert(path.Length, "_");
		}

		private static string ProbeWithFilePointer(string path)
		{
			int position = path.LastIndexOf('/');
			path = path.Remove(position, (path.Length - position));
			return path.Insert(path.Length, "/file.ptr");
		}

		public static HttpWebResponse GetResponseNoException(HttpWebRequest req)
		{
			try
			{
				return (HttpWebResponse)req.GetResponse();
			}
			catch (WebException we)
			{
				var resp = we.Response as HttpWebResponse;

				if (resp == null)
					throw;

				return resp;
			}
		}

		// UserAgent:  Microsoft-Symbol-Server/10.0.10036.206
		// Host:  msdl.microsoft.com
		// URI: /download/symbols/iiscore.pdb/6E3058DA562C4EB187071DC08CF7B59E1/iiscore.pdb
		public string BuildUrl(string filename)
		{
			var meta = MetadataReader.Read(filename);

			if (meta == null || string.IsNullOrEmpty(meta.PdbName))
			{
				return string.Empty;
			}
			else
			{
				var segments = meta.PdbName.Split(new char[] { '\\' });
				var pdbName = segments[segments.Length - 1];

				return this.server + "/" + pdbName + "/" + meta.DebugGUID.ToString("N").ToUpper() + meta.PdbAge + "/" + pdbName;
			}
		}

		private string GetOutputDirectory(Assembly asm)
		{
			if (this.style == OutputStyle.Debugger)
			{
				return this.directory + "\\" + asm.Name + "\\" + asm.PdbGuid;
			}

			return this.directory;
		}

		public DownloadResult DownloadFile(string path)
		{
			bool headVerb = false;
			bool fileptr = false;

			Assembly file = new Assembly(BuildUrl(path));
			string dirPath = GetOutputDirectory(file);

			if (File.Exists(Path.Combine(dirPath, file.Name)))
			{
				return DownloadResult.Exists;
			}

			try
			{
				var req = (HttpWebRequest)System.Net.WebRequest.Create(file.Path);
				req.UserAgent = userAgent;
				var res = GetResponseNoException(req);

				if (res.StatusCode == HttpStatusCode.NotFound)
				{
					res = Retry(file, headVerb);

					if (res.StatusCode == HttpStatusCode.OK)
					{
						file.IsCompressed = true;
					}

					if (res.StatusCode == HttpStatusCode.NotFound)
					{
						res = RetryFilePointer(file);
						fileptr = true;
					}

					if (res.StatusCode != HttpStatusCode.OK)
					{

						FailedFiles[file.Name] = " - " + res.StatusCode + "  " + res.StatusDescription;
					}
				}

				if (res.StatusCode == HttpStatusCode.OK)
				{
					HandleSuccess(res, file, fileptr, dirPath);

					return DownloadResult.Success;
				}
			}
			catch (Exception ex)
			{
				WriteToLog(file.Name, ex);

				return DownloadResult.Error;
			}

			return DownloadResult.NotFound;
		}

		private void HandleSuccess(HttpWebResponse res, Assembly file, bool fileptr, string dirPath)
		{
			Byte[] readBytes = new Byte[this.bufferSize];

			Directory.CreateDirectory(dirPath);

			if (fileptr)
			{
				string filePath = dirPath + "\\" + file.Name;
				string srcFile = null;
				ProcessFileSize(res, out srcFile);

				if (srcFile != null)
				{
					File.Copy(srcFile, filePath);
				}
			}
			else
			{
				if (file.IsCompressed)
				{
					file.Name = ProbeWithUnderscore(file.Name);
				}

				var filePath = dirPath + "\\" + file.Name;
				var writer = new FileStream(filePath, FileMode.Create);
				var stream = res.GetResponseStream();

				stream.CopyTo(writer);

				if (file.IsCompressed)
				{
					HandleCompression(filePath);
				}
			}
		}

		private static void WriteToLog(string fileName, Exception exc)
		{
			WriteToLog(fileName, exc.ToString());
		}

		private static void WriteToLog(string fileName, string text)
		{
			// todo: format in the current directory
			using (FileStream fs = new FileStream("Log.txt", FileMode.Append))
			using (StreamWriter sr = new StreamWriter(fs))
			{
				sr.WriteLine(DateTime.Now.ToString() + "   " + fileName + " - " + text);
			}
		}

		private void HandleCompression(string filePath)
		{
			// out is the same as in, unless it ends in 
			var uncompressedFilePath = filePath;
			if (filePath.EndsWith("_"))
			{
				uncompressedFilePath = filePath.Remove(filePath.Length - 1) + "b";
			}

			string args = string.Format("\"{0}\" \"{1}\"", filePath, uncompressedFilePath);

			ProcessStartInfo startInfo = new ProcessStartInfo("expand", args);

			startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
			startInfo.UseShellExecute = false;
			startInfo.Verb = "runas";
			startInfo.CreateNoWindow = true;

			try
			{
				var started = Process.Start(startInfo);

				started.WaitForExit(600000);
				File.Delete(filePath);
			}
			catch (Exception ex)
			{
				WriteToLog(filePath, ex);
			}
		}
	}
}