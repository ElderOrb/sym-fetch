using System;
using System.IO;

namespace sym_fetch
{
	public static class Program
	{
		public static void Main(string[] args)
		{
			// todo: mono options or the .net contrib console stuff
			// todo: accept output locations other than the current directory
			// todo: accept side-by-side pdb styles
			// todo: ignore files which already have a corresponding pdb
			var downloader = new Downloader(
				directory: Environment.CurrentDirectory
			);

			foreach (var file in Directory.EnumerateFiles(Environment.CurrentDirectory, "*.dll")) {
				var dir = Path.GetDirectoryName(file);
				var name = Path.GetFileNameWithoutExtension(file);

				if (!File.Exists(Path.Combine(dir, name + ".pdb"))) {
					Console.WriteLine($"Fetching symbols for {file}...");

					downloader.DownloadFile(file);
				}
			}
		}
	}
}
