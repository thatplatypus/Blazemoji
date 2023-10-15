namespace Blazemoji.Emojicode
{
    /// <summary>
    /// Base class to represent an Emojicode Keyword
    /// </summary>
    public abstract class EmojicodeKeyword
    {
        public abstract string Emoji { get; }

        public abstract string Description { get; } 

        public abstract string Keyword { get; }

        public abstract string Category { get; }

        public virtual string ShortCode
        {
            get
            {
                return Keyword;
            }
        }

        public virtual string Name
        {
            get
            {
                return GetType().Name;
            }
        }
    }
}
