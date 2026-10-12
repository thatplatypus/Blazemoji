using Blazemoji.Toolchain.Packmoji;

namespace Blazemoji
{
    public static class WebsitePackages
    {
        /// <summary>
        /// Whose packages the website fetches and has built for someone it does not know. A
        /// scope is a GitHub owner, so this is a list of whose releases a visitor can make
        /// this server download and compile.
        /// </summary>
        public const string Scopes = "thatplatypus,emojicode";

        /// <summary>
        /// Makes <see cref="Scopes"/> the list when the host was told none. It is the web
        /// host's own default, in code, so that the safe list does not rest on a variable
        /// someone has to remember to set. Being told a list changes it, and being told an
        /// empty one takes no one's packages.
        /// </summary>
        public static IServiceCollection AddWebsitePackageScopes(this IServiceCollection services) =>
            services.PostConfigure<PackmojiOptions>(packmoji => packmoji.Scopes ??= Scopes);
    }
}
