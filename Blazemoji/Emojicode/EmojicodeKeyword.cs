namespace Blazemoji.Emojicode
{
    /// <summary>
    /// Base class to represent an Emojicode Keyword
    /// </summary>
    public abstract class EmojicodeKeyword
    {
        /// <summary>
        /// The unicode emoji represented as a string, i.e. 🍇
        /// </summary>
        public abstract string Emoji { get; }

        /// <summary>
        /// Short description of what the keyword is.
        /// </summary>
        public abstract string Description { get; } 

        /// <summary>
        /// Similar keyword/concept in a normal language
        /// </summary>
        public abstract string Keyword { get; }

        /// <summary>
        /// Related to the category from the official emojicode docs
        /// </summary>
        public abstract string Category { get; }

        /// <summary>
        /// The idea is this would be able to allow replacements in the editor
        /// IMonarchLanguageInterface may allow this as part of Monarch language json for Monaco
        /// i.e. { => 🍇
        /// </summary>
        public virtual string ShortCode
        {
            get
            {
                return Keyword;
            }
        }

        /// <summary>
        /// Overrideable method, by default returns the class name
        /// </summary>
        public virtual string Name
        {
            get
            {
                return GetType().Name;
            }
        }
    }
}
