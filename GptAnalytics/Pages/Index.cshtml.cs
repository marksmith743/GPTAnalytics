
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GptAnalytics.Repositories;
using GptAnalytics.Models;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure;
using OpenAI.Chat;
using System.IO;
using OfficeOpenXml;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using DocumentFormat.OpenXml.Packaging;

namespace GptAnalytics.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IFileRepository _productRepository;
        private readonly IConfiguration _configuration;

        [TempData]
        public string ChatHistoryJson { get; set; }

        [BindProperty]
        public string UserMessage { get; set; }

        [BindProperty]
        public string GptResponseHtml { get; set; }

        [BindProperty]
        public string GptResponseCSV { get; set; }

        public string TempFolderPath
        {
            get => HttpContext.Session.GetString("TempFolderPath");
            set => HttpContext.Session.SetString("TempFolderPath", value);
        }

        public List<GptAnalytics.Models.ChatMessage> ChatHistory { get; set; } = new();
        public bool IsChatVisible { get; set; } = false;

        [TempData]
        public string UploadedFilesJson { get; set; } // Store the list as JSON in TempData

        public List<string> UploadedFiles
        {
            get => string.IsNullOrEmpty(UploadedFilesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(UploadedFilesJson);
            set => UploadedFilesJson = JsonSerializer.Serialize(value);
        }

        [TempData]
        public string UploadedInstructionFilesJson { get; set; } // Store the list as JSON in TempData

        public List<string> UploadedInstructionFiles
        {
            get => string.IsNullOrEmpty(UploadedInstructionFilesJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(UploadedInstructionFilesJson);
            set => UploadedInstructionFilesJson = JsonSerializer.Serialize(value);
        }



        public IndexModel(ILogger<IndexModel> logger, IFileRepository productRepository, IConfiguration configuration)
        {
            _logger = logger;
            _productRepository = productRepository;
            _configuration = configuration;


        }
        public async Task<IActionResult> OnPostUploadFile(IFormFile uploadedFile)
        {
            // Preserve TempFolderPath across requests if it's already set
            TempData.Keep("TempFolderPath");
            TempData.Keep("ChatHistoryJson");
            TempData.Keep("UploadedFilesJson");
            TempData.Keep("UploadedInstructionFilesJson");

            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                ModelState.AddModelError("UploadedFile", "Please upload a valid file.");
                return Page();
            }
            // Create a new TempFolderPath if needed
            if (string.IsNullOrEmpty(TempFolderPath))
            {
                var rootDirectory = _configuration["GptAnalyticsData:RootDirectory"];
                TempFolderPath = Path.Combine(rootDirectory, Guid.NewGuid().ToString());
                Directory.CreateDirectory(TempFolderPath);
            }

            var filePath = Path.Combine(TempFolderPath, uploadedFile.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await uploadedFile.CopyToAsync(stream);
            }

            if (Path.GetExtension(filePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var csvPath = Path.ChangeExtension(filePath, ".csv");
                ConvertXlsxToCsv(filePath, csvPath);
                System.IO.File.Delete(filePath); // Remove the original XLSX file
                filePath = csvPath;
            }

            var uploadedFiles = UploadedFiles; // Retrieve the current list
            uploadedFiles.Add(Path.GetFileName(filePath)); // Add the new file
            UploadedFiles = uploadedFiles; // Save the updated list

            return Page();
        }

        public async Task<IActionResult> OnPostRemoveFile(string fileName)
        {
            TempData.Keep("TempFolderPath");
            TempData.Keep("ChatHistoryJson");
            TempData.Keep("UploadedFilesJson");
            TempData.Keep("UploadedInstructionFilesJson");
            var filePath = Path.Combine(TempFolderPath, fileName);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);

                var uploadedFiles = UploadedFiles; // Retrieve the current list
                uploadedFiles.Remove(fileName); // Remove the file
                UploadedFiles = uploadedFiles; // Save the updated list
            }

            return Page();
        }
        public async Task<IActionResult> OnPostNewSession()
        {
            if (Directory.Exists(TempFolderPath))
            {
                Directory.Delete(TempFolderPath, true);
            }


            var rootDirectory = _configuration["GptAnalyticsData:RootDirectory"];
            TempFolderPath = Path.Combine(rootDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(TempFolderPath);

            ChatHistory.Clear();
            ChatHistoryJson = string.Empty;
            IsChatVisible = false;
            GptResponseHtml = string.Empty;
            UploadedFiles = new List<string>();
            UploadedInstructionFiles = new List<string>();
            return Page();
        }
        private void ConvertXlsxToCsv(string xlsxPath, string csvPath)
        {
            using (var package = new ExcelPackage(new FileInfo(xlsxPath)))
            {
                var worksheet = package.Workbook.Worksheets[0];
                var csvContent = new StringWriter();

                for (int row = 1; row <= worksheet.Dimension.Rows; row++)
                {
                    for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                    {
                        csvContent.Write(worksheet.Cells[row, col].Text);
                        if (col < worksheet.Dimension.Columns)
                            csvContent.Write(",");
                    }
                    csvContent.WriteLine();
                }

                System.IO.File.WriteAllText(csvPath, csvContent.ToString());
            }
        }

        public async Task OnGet()
        {
            if (!string.IsNullOrEmpty(ChatHistoryJson))
            {
                ChatHistory = JsonSerializer.Deserialize<List<GptAnalytics.Models.ChatMessage>>(ChatHistoryJson);
            }
        }

        public async Task<IActionResult> OnPostSendMessage()
        {
            TempData.Keep("TempFolderPath");
            TempData.Keep("ChatHistoryJson");
            TempData.Keep("UploadedFilesJson");
            TempData.Keep("UploadedInstructionFilesJson");

            if (!string.IsNullOrWhiteSpace(UserMessage))
            {
                if (!string.IsNullOrEmpty(ChatHistoryJson))
                {
                    ChatHistory = JsonSerializer.Deserialize<List<GptAnalytics.Models.ChatMessage>>(ChatHistoryJson);
                }

                ChatHistory.Add(new GptAnalytics.Models.ChatMessage { Sender = "User", Text = UserMessage });
                var chatGptResponse = await GetChatGptResponse(UserMessage, ChatHistory);
                ChatHistory.Add(new GptAnalytics.Models.ChatMessage { Sender = "ChatGPT", Text = chatGptResponse });

                ChatHistoryJson = JsonSerializer.Serialize(ChatHistory); // Save updated history
            }

            IsChatVisible = true;
            return Page();
        }
        public async Task<IActionResult> OnPostUploadInstructionFile(IFormFile instructionFile)
        {
            TempData.Keep("TempFolderPath");
            TempData.Keep("ChatHistoryJson");
            TempData.Keep("UploadedFilesJson");
            TempData.Keep("UploadedInstructionFilesJson");

            if (instructionFile == null || instructionFile.Length == 0)
            {
                ModelState.AddModelError("InstructionFile", "Please upload a valid file.");
                return Page();
            }

            // Create a new TempFolderPath if needed
            if (string.IsNullOrEmpty(TempFolderPath))
            {
                var rootDirectory = _configuration["GptAnalyticsData:RootDirectory"];
                TempFolderPath = Path.Combine(rootDirectory, Guid.NewGuid().ToString());
                Directory.CreateDirectory(TempFolderPath);
            }

            var filePath = Path.Combine(TempFolderPath, instructionFile.FileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await instructionFile.CopyToAsync(stream);
            }

            // Handle .docx files by converting them to .txt
            if (Path.GetExtension(filePath).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                var textContent = ConvertDocxToText(filePath);
                var txtFilePath = Path.ChangeExtension(filePath, ".txt");
                await System.IO.File.WriteAllTextAsync(txtFilePath, textContent);
                System.IO.File.Delete(filePath); // Remove the original .docx file
                filePath = txtFilePath;
            }

            var uploadedFiles = UploadedInstructionFiles; // Retrieve the current list
            uploadedFiles.Add(Path.GetFileName(filePath)); // Add the new file
            UploadedInstructionFiles = uploadedFiles; // Save the updated list

            return Page();
        }

        private string ConvertDocxToText(string docxFilePath)
        {
            using (var stream = new FileStream(docxFilePath, FileMode.Open, FileAccess.Read))
            {
                using (var wordDocument = WordprocessingDocument.Open(stream, false))
                {
                    var body = wordDocument.MainDocumentPart.Document.Body;
                    return body.InnerText;
                }
            }
        }
        private async Task<string> GetChatGptResponse(string userMessage, List<GptAnalytics.Models.ChatMessage> chatHistory)
        {
            var endpoint = new Uri(Environment.GetEnvironmentVariable("AzureOpenAIUrl"));
            var deploymentName = "gpt-4.1-mini";
            var apiKey = Environment.GetEnvironmentVariable("AzureOpenAIKey");

            AzureOpenAIClient azureClient = new(
                endpoint,
                new AzureKeyCredential(apiKey));
            ChatClient chatClient = azureClient.GetChatClient(deploymentName);

            var requestOptions = new ChatCompletionOptions()
            {
                Temperature = 1.0f,
                TopP = 1.0f
            };

            List<OpenAI.Chat.ChatMessage> messages = new List<OpenAI.Chat.ChatMessage>();
            var prompt = "Be sure to display responses as html when describing tables of information, and when doing so, be sure to include the normal  ``` indicators that show that it is html.  However, don't ever show snippets of html without the '''html indicator.   ";
            // Concatenate uploaded instruction files into the prompt
            if (TempFolderPath != null)
            {
                var instructionFiles = Directory.GetFiles(TempFolderPath, "*.txt");
                foreach (var file in instructionFiles)
                {
                    prompt += System.IO.File.ReadAllText(file) + Environment.NewLine;
                }

                var csvs = string.Join(Environment.NewLine, Directory.GetFiles(TempFolderPath, "*.csv").Select(System.IO.File.ReadAllText));
                if (!string.IsNullOrEmpty(csvs))
                {
                    prompt += " " + csvs;
                }
            }
            messages.Add(OpenAI.Chat.ChatMessage.CreateAssistantMessage(prompt));



            foreach (var chatMessage in chatHistory)
            {
                if (chatMessage.Sender == "User")
                {
                    messages.Add(OpenAI.Chat.ChatMessage.CreateUserMessage(chatMessage.Text));
                }
                else
                {
                    messages.Add(OpenAI.Chat.ChatMessage.CreateAssistantMessage(chatMessage.Text));
                }
            }

            var response = await chatClient.CompleteChatAsync(messages, requestOptions);

            var responseText = response.Value.Content[0].Text;

            var startOfHtml = responseText.IndexOf("```html");
            var endOfHtml = responseText.LastIndexOf("```");
            if (startOfHtml >= 0 && endOfHtml > 0)
            {
                GptResponseHtml = responseText.Substring(startOfHtml + 7, endOfHtml - (startOfHtml + 7));

                responseText = responseText.Substring(0, startOfHtml) + " " + responseText.Substring(endOfHtml + 3);
            }

            var startOfCSV = responseText.IndexOf("```csv");
            var endOfCSV = responseText.LastIndexOf("```");
            if (startOfCSV >= 0 && endOfCSV > 0)
            {
                GptResponseCSV = responseText.Substring(startOfCSV + 6, endOfCSV - (startOfCSV + 6));

                var csvFileName = "response.csv";
                var csvFilePath = Path.Combine(TempFolderPath, csvFileName);
                System.IO.File.WriteAllText(csvFilePath, GptResponseCSV);

                GptResponseHtml = $"<html><a target='_blank' href='/Index?handler=DownloadCsv&fileName={csvFileName}'>Download Result File</a></html>";
                responseText = responseText.Substring(0, startOfCSV) + " " + responseText.Substring(endOfCSV + 3);
            }


            return responseText;
            
        }
        public async Task<IActionResult> OnGetDownloadCsv(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(TempFolderPath))
            {
                return NotFound("File not found.");
            }

            var filePath = Path.Combine(TempFolderPath, fileName);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("File not found.");
            }

            var memory = new MemoryStream();
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;

            return File(memory, "text/csv", fileName);
        }
    }


}
