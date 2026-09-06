using System.Text.Json;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// A claim one account signed about itself, as a block anybody can check anywhere.
    /// </summary>
    /// <param name="Address"> The account's address. </param>
    /// <param name="SigningPublicKey"> Base64 signing key, so the block can be checked without asking anybody. </param>
    /// <param name="EncryptionPublicKey"> Base64 encryption key; the address commits to both of them together. </param>
    /// <param name="Claim"> What the account said about itself, in its own words. </param>
    /// <param name="SignedAtUnixMs"> When it was signed; inside the signature, so it cannot be re-dated. </param>
    /// <param name="Signature"> Base64 signature over every field above. </param>
    /// <remarks>
    /// Deliberately not a stored document and not in <c>DataModels</c>. It is written to no collection and no
    /// server ever sees the type: the whole point is an object that leaves this app.
    /// </remarks>
    public sealed record SignedStatement(
        string Address,
        string SigningPublicKey,
        string EncryptionPublicKey,
        string Claim,
        long SignedAtUnixMs,
        string Signature);

    /// <summary>
    /// Signing a sentence about yourself, and checking one somebody else signed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An address is forty-one characters and connects to nothing outside this app. A signed statement is the
    /// first verifiable object somebody can show elsewhere and say "this is me": the checker trusts no server,
    /// opens no account and installs nothing — the keys, the claim and the signature all travel inside the block,
    /// and the address commits to the keys.
    /// </para>
    /// <para>
    /// What it proves is exactly "this address signed this sentence". Who that is, the sentence has to say — which
    /// is why its owner writes it rather than the app.
    /// </para>
    /// </remarks>
    public static class SignedStatementService
    {
        /// <summary> Longest claim accepted; past this it stops being a sentence somebody can read at a glance. </summary>
        public const int MaximumClaimLength = 280;

        /// <summary> Separates this signature's meaning from every other signature the app produces. </summary>
        static readonly byte[] StatementSignatureDomain = "ChaySocial/Statement/v1"u8.ToArray();

        /// <summary> Written out indented, because somebody is going to open this file and look at it. </summary>
        static readonly JsonSerializerOptions StatementJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// <summary> Signs one claim as the given account. </summary>
        /// <param name="owner"> The unlocked account making the claim. </param>
        /// <param name="claim"> What it says about itself; trimmed, and refused when empty or too long. </param>
        /// <returns> The signed block, or null when the claim was not usable. </returns>
        public static SignedStatement? Write(PrivateIdentity owner, string claim)
        {
            string trimmed = claim.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaximumClaimLength) return null;

            long signedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] transcript = BuildTranscript(
                owner.Public.Address,
                owner.Public.SigningPublicKey,
                owner.Public.EncryptionPublicKey,
                trimmed,
                signedAt);

            return new SignedStatement(
                owner.Public.Address,
                Convert.ToBase64String(owner.Public.SigningPublicKey),
                Convert.ToBase64String(owner.Public.EncryptionPublicKey),
                trimmed,
                signedAt,
                Convert.ToBase64String(owner.Sign(transcript)));
        }

        /// <summary>
        /// Checks a block against nothing but itself.
        /// </summary>
        /// <param name="statement"> The block to check. </param>
        /// <returns> True when the address commits to these keys and this signature holds under them. </returns>
        /// <remarks>
        /// The transcript is rebuilt from the block's own fields, so a block whose keys do not hash down to its
        /// address fails without anybody being asked anything — which is what makes this checkable by somebody
        /// who has never heard of this app's servers.
        /// </remarks>
        public static bool Verify(SignedStatement statement)
        {
            try
            {
                byte[] signingKey = Convert.FromBase64String(statement.SigningPublicKey);
                byte[] encryptionKey = Convert.FromBase64String(statement.EncryptionPublicKey);

                if (!AppCryptography.Addresses.Matches(statement.Address, signingKey, encryptionKey)) return false;

                PublicIdentity owner = new(statement.Address, signingKey, encryptionKey);
                byte[] transcript = BuildTranscript(
                    statement.Address, signingKey, encryptionKey, statement.Claim, statement.SignedAtUnixMs);

                return AppCryptography.Identities.Verify(
                    transcript, Convert.FromBase64String(statement.Signature), owner);
            }
            catch (FormatException error)
            {
                Log($"{nameof(SignedStatementService)} was handed a statement with malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Writes a block out as the bytes that go into a file. </summary>
        /// <param name="statement"> The block to write. </param>
        /// <returns> The file's contents. </returns>
        public static byte[] Serialise(SignedStatement statement)
            => JsonSerializer.SerializeToUtf8Bytes(statement, StatementJson);

        /// <summary> Reads a block back, or reports that those bytes were not one. </summary>
        /// <param name="content"> The bytes that were chosen or pasted. </param>
        /// <returns> The block, or null when the bytes are not one. </returns>
        public static SignedStatement? Deserialise(ReadOnlySpan<byte> content)
        {
            try
            {
                SignedStatement? statement = JsonSerializer.Deserialize<SignedStatement>(content, StatementJson);

                return statement is null || statement.Address.Length == 0 || statement.Signature.Length == 0
                    ? null
                    : statement;
            }
            catch (JsonException error)
            {
                Log($"{nameof(SignedStatementService)} was handed bytes that are not a statement.\n{error}", LogLevel.Warning);
                return null;
            }
        }

        /// <summary> Builds the exact bytes an owner signs and a checker verifies. </summary>
        /// <param name="address"> The account's address. </param>
        /// <param name="signingPublicKey"> Its signing key. </param>
        /// <param name="encryptionPublicKey"> Its encryption key. </param>
        /// <param name="claim"> The claim, already trimmed. </param>
        /// <param name="signedAtUnixMs"> When it was signed. </param>
        /// <returns> The transcript to sign. </returns>
        /// <remarks>
        /// Both keys are inside as well as the address, so a block cannot be rebuilt around a different pair of
        /// keys that happen to hash to the same address — and the time is inside so a claim cannot be re-dated.
        /// </remarks>
        static byte[] BuildTranscript(
            string address,
            ReadOnlySpan<byte> signingPublicKey,
            ReadOnlySpan<byte> encryptionPublicKey,
            string claim,
            long signedAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(StatementSignatureDomain);
            transcript.WriteText(address);
            transcript.WriteBytes(signingPublicKey);
            transcript.WriteBytes(encryptionPublicKey);
            transcript.WriteText(claim);
            transcript.WriteInt64(signedAtUnixMs);

            return transcript.ToArray();
        }
    }
}
