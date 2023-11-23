namespace Blazemoji.Contracts.Models
{
    /// <summary>
    /// Represents the result of a system process emojicode action - invoking the compiler or executing an emmojicode binary
    /// </summary>
    public class EmojicodeResult
    {
        /// <summary>
        /// If an error occurred 
        /// </summary>
        public bool Error { get; set; } = false;

        /// <summary>
        /// Additional message, if any
        /// </summary>
        public string Message { get; set; } = "";
        
        /// <summary>
        /// Result of the emojicode requeest
        /// </summary>
        public string Result { get; set; } = "";

        /// <summary>
        /// How long the request took to execute, if applicable
        /// </summary>
        public TimeSpan? ExecutionTime { get; set; } = TimeSpan.Zero;
    }
}
