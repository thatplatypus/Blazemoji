namespace Blazemoji.Test
{
    public class EmojiTypeTests
    {
        [Theory]
        [InlineData(typeof(Grape), "Grape", "🍇", "{")]
        [InlineData(typeof(Watermelon), "Watermelon", "🍉", "}")]
        [InlineData(typeof(ThumbsDown), "ThumbsDown", "👎", "false")]
        [InlineData(typeof(ThumbsUp), "ThumbsUp", "👍", "true")]
        [InlineData(typeof(FistRight), "FistRight", "🤜", "(")]
        [InlineData(typeof(FistLeft), "FistLeft", "🤛", ")")]
        [InlineData(typeof(Return), "Return", "↩️", "return")]
        [InlineData(typeof(Beer), "Beer", "🍺", "!")]
        [InlineData(typeof(Magnet), "Magnet", "🧲", "@")]
        [InlineData(typeof(Exponent), "Exponent", "🔺", "^")]

        public void Emoji_Type_Is_Expected_To_Match_Emojicode(
            Type emojiType, 
            string expectedName, 
            string expectedEmoji, 
            string expectedKeyword)
        {
            //Arrange
            EmojicodeKeyword emoji = (EmojicodeKeyword)Activator.CreateInstance(emojiType)!;

            // Assert
            Assert.Equal(expectedName, emoji.Name);
            Assert.Equal(expectedEmoji, emoji.Emoji);
            Assert.Equal(expectedKeyword, emoji.Keyword);
        }
    }
}