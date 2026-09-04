using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Identity
{
    /// <summary> Why <see cref="ChallengeAuthentication.Validate"/> refused an answer, or <see cref="None"/> when it accepted one. </summary>
    public enum AuthenticationFailure
    {
        /// <summary> The answer was accepted. </summary>
        None,

        /// <summary> The answer was signed over a different challenge than the one this server issued. </summary>
        ChallengeMismatch,

        /// <summary> The answer's timestamp is outside the accepted window — too old to still be fresh, or too far in the future to be honest. </summary>
        OutsideTimeWindow,

        /// <summary> The presented public keys do not hash to the claimed address, so the client is claiming an account it did not derive. </summary>
        AddressKeyMismatch,

        /// <summary> The signature does not verify against the presented signing key. </summary>
        SignatureInvalid
    }

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
        /// Checks an answer. A refusal comes back as a reason rather than an exception, because a failed login is a
        /// normal event a server handles constantly.
        /// </summary>
        /// <param name="request"> What the client sent. </param>
        /// <param name="expectedChallenge"> The challenge this server issued for this attempt. </param>
        /// <param name="identity"> Receives the authenticated identity, or null when the answer was refused. </param>
        /// <param name="validatedAt"> Time to judge freshness against; defaults to now. </param>
        /// <returns> <see cref="AuthenticationFailure.None"/> when the answer was accepted; otherwise why it was refused. </returns>
        public AuthenticationFailure Validate(
            AuthenticationRequest request,
            byte[] expectedChallenge,
            out PublicIdentity? identity,
            DateTimeOffset? validatedAt = null)
        {
            identity = null;

            if (!request.Challenge.AsSpan().SequenceEqual(expectedChallenge)) return AuthenticationFailure.ChallengeMismatch;

            DateTimeOffset now = validatedAt ?? DateTimeOffset.UtcNow;
            TimeSpan age = now - DateTimeOffset.FromUnixTimeSeconds(request.IssuedAtUnixSeconds);
            if (age > _answerValidity || age < -_answerValidity) return AuthenticationFailure.OutsideTimeWindow;

            if (!addressFactory.Matches(request.Address, request.SigningPublicKey, request.EncryptionPublicKey))
            {
                return AuthenticationFailure.AddressKeyMismatch;
            }

            byte[] transcript = BuildTranscript(
                request.Address,
                request.SigningPublicKey,
                request.EncryptionPublicKey,
                request.Challenge,
                request.IssuedAtUnixSeconds);

            PublicIdentity candidate = new(request.Address, request.SigningPublicKey, request.EncryptionPublicKey);
            if (!scheme.Verify(transcript, request.Signature, candidate)) return AuthenticationFailure.SignatureInvalid;

            identity = candidate;
            return AuthenticationFailure.None;
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

