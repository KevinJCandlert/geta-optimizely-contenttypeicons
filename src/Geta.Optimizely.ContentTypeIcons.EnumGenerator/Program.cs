﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Geta.Optimizely.ContentTypeIcons.EnumGenerator.Models;
using Newtonsoft.Json;

namespace Geta.Optimizely.ContentTypeIcons.EnumGenerator
{
    static class Program
    {
        static async Task Main()
        {
            var sourcePath = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.Parent?.Parent?.FullName;
            if (string.IsNullOrEmpty(sourcePath))
            {
                Console.WriteLine("Unable to find source path.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Font Awesome Enum Generator");
            Console.WriteLine("{0}", sourcePath);
            var enumBasePath = $@"{sourcePath}\Geta.Optimizely.ContentTypeIcons";
            Console.WriteLine("\nOutput directory: {0}", enumBasePath);

            var fontAwesomeZipStream = await GithubDownloader.DownloadLatestReleaseAsync("FortAwesome", "Font-Awesome");

            using (var archive = new ZipArchive(fontAwesomeZipStream))
            {
                var rootEntry = archive.Entries[0];
                var metaDataEntry = archive.GetEntry(rootEntry + "metadata/icons.json");

                if (metaDataEntry == null)
                {
                    Console.WriteLine("\nArchive does not contain a metadata directory with a icons.json file.");
                    return;
                }

                Console.WriteLine("\nLoading metadata from: {0}", metaDataEntry);
                var metadata = LoadMetadata(metaDataEntry.Open());

                // Get a list all the different styles
                var styles = metadata.SelectMany(x => x.Styles).Distinct();

                foreach (var item in styles)
                {
                    var styleName = item.ToTitleCase();
                    var enumName = $"FontAwesome5{styleName}";
                    var localPath = $@"{enumBasePath}\{enumName}.cs";

                    Console.WriteLine("\nGenerating {0}.cs...", enumName);
                    var icons = metadata.Where(x => x.Styles.Contains(item) && !x.Private).ToList();
                    WriteEnumToFile(localPath, enumName, icons);
                }

                CopyFontFiles(archive, enumBasePath);
                CopyCssFiles(archive, enumBasePath);
            }

            Console.WriteLine("\nDone generating Enums. Press enter to exit.");
            Console.ReadLine();
        }

        private static void CopyFontFiles(ZipArchive archive, string enumBasePath)
        {
            var destination = $@"{enumBasePath}\module\ClientResources\fa5\webfonts\";
            var rootEntry = archive.Entries[0];
            var fontEntries = archive.Entries.Where(x =>
                                                        x.FullName.StartsWith(rootEntry + "webfonts") &&
                                                        x.FullName.Contains("."));

            foreach (var fileToCopy in fontEntries)
            {
                Console.WriteLine("\nCopying {0} to {1}...", fileToCopy.Name, destination);
                fileToCopy.ExtractToFile(destination + Path.GetFileName(fileToCopy.Name), true);
            }
        }

        private static void CopyCssFiles(ZipArchive archive, string enumBasePath)
        {
            var destination = $@"{enumBasePath}\module\ClientResources\fa5\css\";
            var rootEntry = archive.Entries[0];
            var cssFile = archive.Entries.Single(x => x.FullName.Contains(rootEntry + "css/all.min.css"));

            Console.WriteLine("\nCopying {0} to {1}...", cssFile.Name, destination);
            cssFile.ExtractToFile(destination + Path.GetFileName(cssFile.Name), true);
        }

        private static IList<MetadataIcon> LoadMetadata(Stream stream)
        {
            using var file = new StreamReader(stream);

            var serializer = new JsonSerializer();
            serializer.Converters.Add(new FontAwesomeJsonConverter());
            return (List<MetadataIcon>)serializer.Deserialize(file, typeof(List<MetadataIcon>));
        }

        private static void WriteEnumToFile(string path, string enumName, IReadOnlyCollection<MetadataIcon> icons)
        {
            var latestVersionChange = icons.SelectMany(x => x.Changes).Distinct().OrderBy(x => x).Last();

            using var writer = new StreamWriter(path);
            
            writer.WriteLine("//------------------------------------------------------------------------------");
            writer.WriteLine("// <auto-generated>");
            writer.WriteLine("//     This code was generated by a tool.");
            writer.WriteLine("//");
            writer.WriteLine("//     Changes to this file may cause incorrect behavior and will be lost if");
            writer.WriteLine("//     the code is regenerated.");
            writer.WriteLine("// </auto-generated>");
            writer.WriteLine("//------------------------------------------------------------------------------");
            writer.WriteLine();

            writer.WriteLine("namespace Geta.Optimizely.ContentTypeIcons\n{");

            writer.WriteLine("    /// <summary>");
            writer.WriteLine($"    /// Font Awesome. Version {latestVersionChange}.");
            writer.WriteLine("    /// </summary>");

            writer.WriteLine($"    public enum {enumName}");
            writer.WriteLine("    {");

            foreach (var icon in icons)
            {
                writer.WriteLine("        /// <summary>");
                writer.WriteLine(
                    $"        /// {SecurityElement.Escape(icon.Label.ToTitleCase())} ({SecurityElement.Escape(icon.Name)})");
                WriteStyles(writer, icon);
                WriteSearchTerms(writer, icon);
                WriteChanges(writer, icon);
                writer.WriteLine("        /// </summary>");

                var name = GetEnumSafeName(icon);
                writer.WriteLine($"        {name} = 0x{icon.Unicode},\n");
            }

            writer.WriteLine("    }\n}");
        }

        private static void WriteStyles(StreamWriter writer, MetadataIcon icon)
        {
            if (icon.Styles?.Count > 1)
            {
                writer.WriteLine(
                    $"        /// <para>Styles: {SecurityElement.Escape(string.Join(", ", icon.Styles))}</para>");
            }
        }

        private static void WriteSearchTerms(StreamWriter writer, MetadataIcon icon)
        {
            if (icon.Search?.Terms?.Count > 0)
            {
                writer.WriteLine(
                    $"        /// <para>Terms: {SecurityElement.Escape(string.Join(", ", icon.Search.Terms))}</para>");
            }
        }

        private static void WriteChanges(StreamWriter writer, MetadataIcon icon)
        {
            var changes = icon.Changes.Select(x => x.FormatSemver()).OrderBy(x => x).ToList();

            writer.Write($"        /// <para>Added in {changes.First()}");
            if (changes.Count > 1)
            {
                var otherChanges = changes.Skip(1).ToList();
                var result = string.Join(", ", otherChanges.Take(otherChanges.Count - 1)) +
                             (otherChanges.Count > 1 ? " and " : string.Empty) + otherChanges.LastOrDefault();
                writer.Write($", updated in {result}");
            }

            writer.Write(".</para>\n");
        }

        private static string GetEnumSafeName(MetadataIcon icon)
        {
            var name = icon.Name.Replace('-', ' ');
            name = name.ToTitleCase();
            name = name.Replace(" ", string.Empty);
            if (char.IsDigit(name[0]))
            {
                name = "_" + name;
            }

            // Verify reverse conversion
            var reverse = name.ToDashCase().Replace("_", string.Empty);
            if (!icon.Name.Equals(reverse))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{icon.Name}\t!=\t{reverse}\t{name}");
                Console.ForegroundColor = ConsoleColor.White;
            }

            return name;
        }
    }
}
