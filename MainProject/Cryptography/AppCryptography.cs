using ChaySocial.MainProject.Identity;

namespace ChaySocial.MainProject.Cryptography
{
    /// <summary>
    /// The one place the app decides which algorithms it runs on. Every other file asks this class instead of
    /// constructing a scheme, so switching to a different signature scheme, cipher or address prefix is a change
    /// here and nowhere else.
    /// </summary>
    public static class AppCryptography
    {
        /// <summary> Label every account address starts with. </summary>
        public const string AddressPrefix = "chay";

        /// <summary> Fast expander used wherever one secret has to become several keys. </summary>
        public static readonly IKeyDerivation SeedExpander = new HkdfKeyDerivation();

        /// <summary> Cipher protecting message bodies and sealed identities. </summary>
        public static readonly IAeadCipher Cipher = new ChaCha20Poly1305Cipher();

        /// <summary> Signing: a classical scheme and a post-quantum one, both of which must verify. </summary>
        public static readonly ISignatureScheme Signatures =
            new HybridSignatureScheme(new Ed25519SignatureScheme(), new MLDsaSignatureScheme(), SeedExpander);

        /// <summary> Key agreement: a classical scheme and a post-quantum one, whose secrets are mixed into one key. </summary>
        public static readonly IKeyEncapsulation KeyExchange =
            new HybridKeyEncapsulation(new X25519KeyEncapsulation(SeedExpander), new MLKemKeyEncapsulation(), SeedExpander);

        /// <summary> Turns public keys into account addresses and checks that an address belongs to a set of keys. </summary>
        public static readonly IdentityAddressFactory Addresses = new(AddressPrefix);

        /// <summary> Creates and reopens accounts from a master seed. </summary>
        public static readonly IdentityScheme Identities = new(Signatures, KeyExchange, SeedExpander, Addresses);

        /// <summary> Locks a master seed behind a passphrase. </summary>
        public static readonly IdentityVault Vault = new(new Argon2idKeyDerivation(), Cipher);

        /// <summary> Signs and checks login answers. </summary>
        public static readonly ChallengeAuthentication Authentication = new(Identities, Addresses);
    }
}
