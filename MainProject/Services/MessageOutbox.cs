using System.Buffers.Binary;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary> One letter waiting to go out, and when it will. </summary>
    /// <param name="Id"> Something to name it by while it waits; it has no message id until it is sent. </param>
    /// <param name="RecipientAddress"> Who it is for, so a conversation can show only its own waiting letters. </param>
    /// <param name="Text"> What was written. </param>
    /// <param name="DispatchAtUnixMs"> When it will go. </param>
    /// <remarks>
    /// It carries everything a letter is sent with rather than only its words. A letter with a picture on it, or
    /// one meant to be read once, is exactly the kind whose arrival time is worth hiding — leaving those to go
    /// straight out would put the hole back where it mattered most.
    /// </remarks>
    public readonly record struct WaitingLetter(string Id, string RecipientAddress, string Text, long DispatchAtUnixMs)
    {
        /// <summary> True when this letter is meant to be read once. </summary>
        public bool IsVanishing { get; init; }

        /// <summary> Media already uploaded for it. </summary>
        public IReadOnlyList<MediaAttachment> Attachments { get; init; } = [];

        /// <summary> Letter it replies to, or empty. </summary>
        public string QuotedMessageId { get; init; } = string.Empty;
    }

    /// <summary>
    /// Holds a letter for a moment before it goes, so the gap between two letters is not a fact about the writer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sealing the words and rounding the stored clock take away what a letter says and when it was written. What
    /// is left is when it arrived: a server watching its own inbox still sees one letter land and another land
    /// straight after it. A short, random wait before dispatch breaks that, and it costs nobody anything they can
    /// feel.
    /// </para>
    /// <para>
    /// A queue in memory dies with the browser tab, and a letter that quietly never went is worse than no delay at
    /// all. So the wait is shown rather than hidden, and it can always be skipped: this is a courtesy the app
    /// performs in the open, not a promise it cannot keep.
    /// </para>
    /// </remarks>
    public static class MessageOutbox
    {
        /// <summary> Shortest a letter waits. </summary>
        public const int SmallestDispatchDelaySeconds = 3;

        /// <summary> Longest a letter waits. Short enough that a conversation still feels like one. </summary>
        public const int LargestDispatchDelaySeconds = 20;

        /// <summary> Bytes read to draw one delay. </summary>
        const int DelayDrawByteCount = sizeof(int);

        /// <summary> Random bytes behind a waiting letter's name. </summary>
        const int WaitingIdBytes = 8;

        /// <summary> How often the queue looks at the clock while a letter waits. </summary>
        const int TickMilliseconds = 250;

        /// <summary> Letters waiting, oldest first. </summary>
        static readonly List<WaitingLetter> Waiting = [];

        /// <summary> Everything still waiting, for a screen that wants to draw it. </summary>
        public static IReadOnlyList<WaitingLetter> All => [.. Waiting];

        /// <summary> Everything waiting for one account. </summary>
        /// <param name="recipientAddress"> The account being written to. </param>
        /// <returns> That conversation's waiting letters, in the order they were written. </returns>
        public static IReadOnlyList<WaitingLetter> For(string recipientAddress)
            => [.. Waiting.Where(letter => letter.RecipientAddress == recipientAddress)];

        /// <summary>
        /// Takes a letter and sends it after a short random wait.
        /// </summary>
        /// <param name="sender"> The unlocked account writing it. </param>
        /// <param name="recipientProfile"> Profile of the account being written to. </param>
        /// <param name="text"> What was written. </param>
        /// <param name="isVanishing"> True when it is meant to be read once. </param>
        /// <param name="attachments"> Media already uploaded for it. </param>
        /// <param name="quotedMessageId"> Letter it replies to, or empty. </param>
        /// <returns> The letter as it waits, so the screen can draw it counting down. </returns>
        public static WaitingLetter Hold(
            PrivateIdentity sender,
            ProfileData recipientProfile,
            string text,
            bool isVanishing = false,
            IReadOnlyList<MediaAttachment>? attachments = null,
            string quotedMessageId = "")
        {
            long dispatchAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + DrawDelayMilliseconds();
            WaitingLetter letter = new(
                Text.Base32.Encode(RandomSource.Next(WaitingIdBytes)),
                recipientProfile.Address,
                text,
                dispatchAt)
            {
                IsVanishing = isVanishing,
                Attachments = attachments ?? [],
                QuotedMessageId = quotedMessageId
            };

            Waiting.Add(letter);
            MainEvents.Trigger(MainEvents.Names.OutboxChanged, letter.RecipientAddress);

            // Deliberately not awaited: the writer's screen carries on and the letter leaves on its own.
            _ = DispatchWhenDueAsync(sender, recipientProfile, letter);

            return letter;
        }

        /// <summary> Sends one waiting letter now, for a writer who would rather not wait. </summary>
        /// <param name="id"> The waiting letter's name. </param>
        public static void SendNow(string id)
        {
            int index = Waiting.FindIndex(letter => letter.Id == id);
            if (index < 0) return;

            Waiting[index] = Waiting[index] with { DispatchAtUnixMs = 0 };
            MainEvents.Trigger(MainEvents.Names.OutboxChanged, Waiting[index].RecipientAddress);
        }

        /// <summary>
        /// Empties the queue. Called when the session changes, so an account that has just been switched to does
        /// not inherit somebody else's unsent post.
        /// </summary>
        public static void Forget()
        {
            if (Waiting.Count == 0) return;

            Waiting.Clear();
            MainEvents.Trigger(MainEvents.Names.OutboxChanged);
        }

        /// <summary> Waits for a letter's moment and then sends it. </summary>
        /// <param name="sender"> The unlocked account writing it. </param>
        /// <param name="recipientProfile"> Profile of the account being written to. </param>
        /// <param name="letter"> The letter as it was taken. </param>
        /// <returns> A task that completes once the letter has gone or been given up on. </returns>
        static async Task DispatchWhenDueAsync(PrivateIdentity sender, ProfileData recipientProfile, WaitingLetter letter)
        {
            try
            {
                // The clock is looked at rather than slept through in one go, so "send it now" is felt within a
                // quarter of a second and a queue emptied by a sign-out stops on its own.
                long lastSecondsShown = -1;

                while (true)
                {
                    int index = Waiting.FindIndex(waiting => waiting.Id == letter.Id);
                    if (index < 0) return;

                    long left = Waiting[index].DispatchAtUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (left <= 0) break;

                    // Announced once per second rather than on every look, so a screen showing the countdown
                    // redraws exactly as often as the number it is drawing changes.
                    long secondsShown = (left + 999) / 1000;
                    if (secondsShown != lastSecondsShown)
                    {
                        lastSecondsShown = secondsShown;
                        MainEvents.Trigger(MainEvents.Names.OutboxChanged, letter.RecipientAddress);
                    }

                    await Task.Delay(TickMilliseconds);
                }

                Waiting.RemoveAll(waiting => waiting.Id == letter.Id);

                await MessageService.SendAsync(
                    sender,
                    recipientProfile,
                    letter.Text,
                    letter.IsVanishing,
                    letter.Attachments,
                    letter.QuotedMessageId);
            }
            catch (Exception error)
            {
                Waiting.RemoveAll(waiting => waiting.Id == letter.Id);
                Log($"A held letter to '{letter.RecipientAddress}' could not be sent.\n{error}", LogLevel.Error);
            }
            finally
            {
                MainEvents.Trigger(MainEvents.Names.OutboxChanged, letter.RecipientAddress);
            }
        }

        /// <summary>
        /// Draws one wait, from the same source the app draws keys from.
        /// </summary>
        /// <returns> How long this letter waits, in milliseconds. </returns>
        /// <remarks>
        /// <see cref="RandomSource"/> hands back bytes and nothing else, so four of them are read as one number and
        /// folded into the range. A predictable wait would be no wait at all to anybody timing the arrivals.
        /// </remarks>
        static long DrawDelayMilliseconds()
        {
            uint drawn = BinaryPrimitives.ReadUInt32BigEndian(RandomSource.Next(DelayDrawByteCount));
            int span = LargestDispatchDelaySeconds - SmallestDispatchDelaySeconds + 1;

            return (SmallestDispatchDelaySeconds + (drawn % span)) * 1000L;
        }
    }
}
