namespace Blazemoji.Contracts.Models
{
    public class ExecuteCodeResult
    {

        public bool Error { get; set; } = false;

        public string Message { get; set; } = "";
        public string Result { get; set; } = "";

        public TimeSpan? ExecutionTime { get; set; } = TimeSpan.Zero;
    }
}
