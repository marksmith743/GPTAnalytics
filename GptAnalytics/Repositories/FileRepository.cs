using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using GptAnalytics.Models;

namespace GptAnalytics.Repositories
{
    public interface IFileRepository
    {
        List<string> ReadCsvFilesFromFolder(string folderPath);
    }

    public class FileRepository : IFileRepository
    {
        public List<string> ReadCsvFilesFromFolder(string folderPath)
        {
            var csvContents = new List<string>();

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"The folder path '{folderPath}' does not exist.");
            }

            var csvFiles = Directory.GetFiles(folderPath, "*.csv");

            var counter = 0;
            foreach (var file in csvFiles)
            {
                counter++;
                var content = File.ReadAllText(file);
                csvContents.Add($" Here is CSV #{counter}" + content + "              ");
            }

            return csvContents;
        }
    }
}
