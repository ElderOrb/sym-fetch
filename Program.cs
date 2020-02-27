using System;
using System.Collections.Generic;
using System.IO;
using Mono.Options;

namespace sym_fetch
{
	public static class Program
	{
		private static string input;
		private static string output;
		private static OutputStyle style;

		public enum DeleteOptions {
			None = 0,
			NotFound = 1, 
			NoSymbols = 2,

			DryRun = 4
		}

		public static DeleteOptions deleteOptions = DeleteOptions.None;

		private static readonly OptionSet options = new OptionSet()
		{
			{ "i|in=", "The input directory to probe for assemblies", (s) => { input = s; } },
			{ "o|out=", "The symbol desitination directory", (s) => { output = s; } },
			{ "d|del=", "Delete binaries without related symbols", (s) => { SetDeleteOptions(s); } },
			{ "s|style=", "The output path style [ Debugger | SideBySide ]", (s) => { SetStyle(s); } },
			{ "h|?|help", "display the command line help", (_) => { ShowHelpAndExit(null); } }
		};

		private static void SetStyle(string style)
		{
			if (!Enum.TryParse(typeof(OutputStyle), style, true, out object obj))
			{
				Console.Error.WriteLine("Invlid output style '" + style + "'");
				ShowHelpAndExit();
			}

			Program.style = (OutputStyle)obj;
		}

		private static void SetDeleteOptions(string options)
		{
			var opts = options.Split("+");
			DeleteOptions deleteOptions = DeleteOptions.None;
			foreach(var opt in opts) {
				if(Enum.TryParse<DeleteOptions>(opt, out var deleteOption)) {
					deleteOptions |= deleteOption;
					Console.WriteLine($"Delete option: {deleteOption}");
				}
			}

			Program.deleteOptions = deleteOptions;
		}

		private static void ShowHelpAndExit(IEnumerable<string> errors = null)
		{

			if (errors != null)
			{
				foreach (var err in errors)
				{
					Console.Out.WriteLine(err);
				}
			}

			Console.WriteLine("Usage: sym-fetch [options]\n");
			Console.WriteLine("Options");
			options.WriteOptionDescriptions(Console.Out);
			Environment.Exit(-1);
		}

		static void Delete(string file) {
			try {
				if(!Program.deleteOptions.HasFlag(DeleteOptions.DryRun)) {
					Console.WriteLine($"Deleting... {file}");
					File.Delete(file);
					Console.WriteLine($"Deleting... {file} succeed");
				} else {
					Console.WriteLine($"Fake deleting... {file}");
				}
			}
			catch(Exception ex) {
				Console.WriteLine($"Deleting... {file} failed: {ex}");
			}
		}

		public static void Main(string[] args)
		{
			Program.input = Environment.CurrentDirectory;
			Program.output = Environment.CurrentDirectory;
			Program.style = OutputStyle.SideBySide;

			var errors = options.Parse(args);

			if (errors != null && errors.Count > 0)
			{
				ShowHelpAndExit(errors);
			}

			var downloader = new Downloader(directory: output, style: style);

			foreach (var file in Directory.EnumerateFiles(input, "*.dll", SearchOption.AllDirectories))
			{
				Console.Write($"Fetching {file}... ");

				var res = downloader.DownloadFile(file);

				if (res == DownloadResult.NotFound)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"Symbols not found");

					if(Program.deleteOptions.HasFlag(DeleteOptions.NotFound)) {
						Delete(file);
					}
				}
				else if (res == DownloadResult.Exists)
				{
					Console.ForegroundColor = ConsoleColor.DarkGray;
					Console.WriteLine($"Symbols already exist");
				}
				else if (res == DownloadResult.Success)
				{
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine("Success");
				}
				else if(res == DownloadResult.NoSymbols)
				{
					Console.ForegroundColor = ConsoleColor.DarkRed;
					Console.WriteLine("No symbols found");

					if(Program.deleteOptions.HasFlag(DeleteOptions.NoSymbols)) {
						Delete(file);
					}
				}

				Console.ResetColor();
			}
		}
	}
}
