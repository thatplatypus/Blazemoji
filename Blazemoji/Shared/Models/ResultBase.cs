namespace Blazemoji.Shared.Models
{
    public class ResultBase
    {

        public bool Error { get; set; }

        public string Message { get; set; } = "";
        public string Result { get; set; } = "";
    }
}