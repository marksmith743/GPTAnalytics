using System;
using System.Collections.Generic;

namespace GptAnalytics.Models
{
    // Renamed the class to avoid conflict with the existing 'Project' class in the same namespace.  
    public class Project
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string InputFolder { get; set; }
        public string OutputFolder { get; set; }
        public string Instructions { get; set; }
    }
    public class ChatMessage
    {
        public string Sender { get; set; }
        public string Text { get; set; }
    }
}
