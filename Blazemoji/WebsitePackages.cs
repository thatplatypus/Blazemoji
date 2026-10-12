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
        /// Where the website finds packages: Packmoji's registry, which says which versions
        /// there are and which bytes each is, and can withdraw one. Told an empty address, the
        /// website finds packages on GitHub itself.
        /// </summary>
        public const string Registry = "https://raw.githubusercontent.com/thatplatypus/Packmoji/registry";

        /// <summary>
        /// Makes <see cref="Scopes"/> the list, and <see cref="Registry"/> where packages are
        /// found, when the host was told neither. They are the web host's own defaults, in
        /// code, so that the safe list does not rest on a variable someone has to remember
        /// to set. Being told a list changes it, and being told an empty one takes no one's
        /// packages. Being told a registry changes that, and being told an empty one has
        /// packages found on GitHub itself.
        /// </summary>
        public static IServiceCollection AddWebsitePackages(this IServiceCollection services) =>
            services.PostConfigure<PackmojiOptions>(packmoji =>
            {
                packmoji.Scopes ??= Scopes;
                packmoji.Registry ??= Registry;
            });
    }
}
