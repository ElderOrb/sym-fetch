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

		private static readonly OptionSet options = new OptionSet()
		{
			{ "i|in=", "The input directory to probe for assemblies", (s) => { input = s; } },
			{ "o|out=", "The symbol desitination directory", (s) => { output = s; } },
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

			// todo: mono options or the .net contrib console stuff
			// todo: accept output locations other than the current directory
			// todo: accept side-by-side pdb styles
			// todo: ignore files which already have a corresponding pdb
			var downloader = new Downloader(
				directory: Environment.CurrentDirectory,
				style: style
			);

			foreach (var file in Directory.EnumerateFiles(Environment.CurrentDirectory, "*.dll"))
			{
				var dir = Path.GetDirectoryName(file);
				var name = Path.GetFileNameWithoutExtension(file);

				Console.Write($"Fetching {file}... ");

				var res = downloader.DownloadFile(file);

				if (res == DownloadResult.NotFound)
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					Console.WriteLine($"Symbols not found");
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

				Console.ResetColor();
			}
		}
	}
}
