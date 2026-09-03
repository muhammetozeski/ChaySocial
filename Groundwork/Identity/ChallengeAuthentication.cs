using Groundwork.Cryptography;
using Groundwork.Outcomes;
using Groundwork.Text;

namespace Groundwork.Identity
{
    /// <summary>
    /// What a client sends to prove it holds an account, and what a server checks. It carries public keys and a
    /// signature — never a password, never the seed — so an attacker who records it, or a server that logs it, still
    /// cannot sign as the account tomorrow.
    /// </summary>
    /// <param name="Address"> Address the client claims. </param>
    /// <param name="SigningPublicKey"> Key the server verifies the signature with, and hashes back into the address. </param>
    /// <param name="EncryptionPublicKey"> Key the address also commits to; presented so the server can recompute the address. </param>
    /// <param name="Challenge"> The random value the server issued for this attempt. </param>
    /// <param name="IssuedAtUnixSeconds"> When the client answered, so a stale answer can be refused. </param>
    /// <param name="Signature"> Signature over all of the above. </param>
    public sealed record AuthenticationRequest(
        string Address,
        byte[] SigningPublicKey,
        byte[] EncryptionPublicKey,
        byte[] Challenge,
        long IssuedAtUnixSeconds,
        byte[] Signature);

    /// <summary>
    /// Challenge-response login. The server hands out a random challenge, the client signs it, and the server
    /// verifies the signature and recomputes the address from the presented keys. Nothing secret ever crosses the
    /// wire, so there is no password database to steal and no credential a compromised server could reuse.
    /// </summary>
    /// <param name="scheme"> Identity scheme whose signatures and addresses are being checked. </param>
    /// <param name="addressFactory"> Recomputes an address from the presented public keys. </param>
    /// <param name="answerValidity"> How long an answer stays acceptable, bounding how long a captured one is worth replaying. </param>
    public sealed class ChallengeAuthentication(
        IdentityScheme scheme,
        IdentityAddressFactory addressFactory,
        TimeSpan? answerValidity = null)
    {
        static readonly byte[] TranscriptDomain = "Groundwork/ChallengeAuthentication/v1"u8.ToArray();

        /// <summary> 256 bits of challenge — far beyond what an attacker could pre-compute answers for. </summary>
        public const int ChallengeSize = 32;

        /// <summary> Window an answer stays valid for when the caller does not choose one. </summary>
        public static readonly TimeSpan DefaultAnswerValidity = TimeSpan.FromMinutes(2);

        readonly TimeSpan _answerValidity = answerValidity ?? DefaultAnswerValidity;

        /// <summary> Draws a fresh challenge for one login attempt. The server must remember it and accept it once. </summary>
        /// <returns> A random challenge of <see cref="ChallengeSize"/> bytes. </returns>
        public static byte[] CreateChallenge() => RandomSource.Next(ChallengeSize);

        /// <summary> Answers a challenge as the given account. </summary>
        /// <param name="identity"> The unlocked account proving itself. </param>
        /// <param name="challenge"> Challenge the server issued. </param>
        /// <param name="answeredAt"> Time to stamp into the answer; defaults to now. </param>
        /// <returns> The request to send to the server. </returns>
        public AuthenticationRequest Answer(PrivateIdentity identity, byte[] challenge, DateTimeOffset? answeredAt = null)
        {
            long issuedAt = (answeredAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

            byte[] transcript = BuildTranscript(
                identity.Public.Address,
                identity.Public.SigningPublicKey,
                identity.Public.EncryptionPublicKey,
                challenge,
                issuedAt);

            return new AuthenticationRequest(
                identity.Public.Address,
                identity.Public.SigningPublicKey,
                identity.Public.EncryptionPublicKey,
                challenge,
                issuedAt,
                identity.Sign(transcript));
        }

        /// <summary>
        /// Checks an answer. Every failure returns a reason rather than throwing, because a failed login is a normal
        /// event a server handles thousands of times a day.
        /// </summary>
        /// <param name="request"> What the client sent. </param>
        /// <param name="expectedChallenge"> The challenge this server issued for this attempt. </param>
        /// <param name="validatedAt"> Time to judge freshness against; defaults to now. </param>
        /// <returns> The authenticated public identity on success; the reason it was refused otherwise. </returns>
        public Result<PublicIdentity> Validate(AuthenticationRequest request, byte[] expectedChallenge, DateTimeOffset? validatedAt = null)
        {
            if (!request.Challenge.AsSpan().SequenceEqual(expectedChallenge))
            {
                return Result<PublicIdentity>.Failure("The answer does not match the challenge that was issued.");
            }

            DateTimeOffset now = validatedAt ?? DateTimeOffset.UtcNow;
            TimeSpan age = now - DateTimeOffset.FromUnixTimeSeconds(request.IssuedAtUnixSeconds);
            if (age > _answerValidity || age < -_answerValidity)
            {
                return Result<PublicIdentity>.Failure("The answer is outside the accepted time window.");
            }

            if (!addressFactory.Matches(request.Address, request.SigningPublicKey, request.EncryptionPublicKey))
            {
                return Result<PublicIdentity>.Failure("The presented keys do not belong to the claimed address.");
            }

            byte[] transcript = BuildTranscript(
                request.Address,
                request.SigningPublicKey,
                request.EncryptionPublicKey,
                request.Challenge,
                request.IssuedAtUnixSeconds);

            PublicIdentity identity = new(request.Address, request.SigningPublicKey, request.EncryptionPublicKey);

            return scheme.Verify(transcript, request.Signature, identity)
                ? Result<PublicIdentity>.Success(identity)
                : Result<PublicIdentity>.Failure("The signature does not verify against the presented key.");
        }

        /// <summary> Builds the exact bytes both sides sign and verify. </summary>
        /// <param name="address"> Claimed address. </param>
        /// <param name="signingPublicKey"> Presented signing key. </param>
        /// <param name="encryptionPublicKey"> Presented encryption key. </param>
        /// <param name="challenge"> Challenge being answered. </param>
        /// <param name="issuedAtUnixSeconds"> Timestamp stamped into the answer. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(string address, byte[] signingPublicKey, byte[] encryptionPublicKey, byte[] challenge, long issuedAtUnixSeconds)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(TranscriptDomain);
            transcript.WriteText(address);
            transcript.WriteBytes(signingPublicKey);
            transcript.WriteBytes(encryptionPublicKey);
            transcript.WriteBytes(challenge);
            transcript.WriteInt64(issuedAtUnixSeconds);
            return transcript.ToArray();
        }
    }
}
