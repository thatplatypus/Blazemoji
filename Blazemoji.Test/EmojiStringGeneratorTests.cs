using Microsoft.AspNetCore.Routing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Blazemoji.Test
{
    public class EmojiStringGeneratorTests
    {
        [Fact]
        public void EmojiKey_IsValid_Format()
        {
            var emojiString = EmojiStringGenerator.GetEmojiKey();
            var regex = new Regex(@"^.{16}-.{8}-.{8}-.{8}-.{24}$");
            Assert.Matches(regex, emojiString);
        }

        [Theory]
        [InlineData(1_000_000)]
        public void EmojiKey_CanGenerate_LargeNumbersCollisionless(long keysToGenerate)
        {
            bool collision = false;
            var used = new HashSet<string>();

            for(long i = 0; i < keysToGenerate; i++)
            {
                if(!used.Add(EmojiStringGenerator.GetEmojiKey()))
                {
                    collision = true;
                    break;
                }

            }

            Assert.False(collision);

        }
    }
}
