namespace Blazemoji.Test
{
    public class EmojiTypeTests
    {
        [Theory]
        [InlineData(typeof(Grape), "Grape", "🍇", "{")]
        [InlineData(typeof(Watermelon), "Watermelon", "🍉", "}")]
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