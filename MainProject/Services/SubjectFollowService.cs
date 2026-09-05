using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Following a subject rather than a person.
    /// </summary>
    /// <remarks>
    /// Until now the only way to keep up with something was to guess who writes about it and take everything else
    /// they say along with it. A subject follow is a reader stating their own interest in one sentence — "posts
    /// whose text names this subject, newest first" — with nothing ranking anything on their behalf.
    /// </remarks>
    public static class SubjectFollowService
    {
        /// <summary> Most subjects read back for one account. </summary>
        public const int SubjectsPerReader = 200;

        /// <summary> Starts following a subject. </summary>
        /// <param name="follower"> The unlocked account declaring the interest. </param>
        /// <param name="subject"> Subject as it was written, with or without its mark. </param>
        /// <returns> True when the subject is now followed. </returns>
        public static async Task<bool> FollowAsync(PrivateIdentity follower, string subject)
        {
            string stored = Normalise(subject);
            if (stored.Length == 0) return false;

            await AppServices.Documents.WriteAsync(
                SubjectFollowData.IdFor(follower.Public.Address, stored),
                new SubjectFollowData
                {
                    FollowerAddress = follower.Public.Address,
                    Subject = stored,
                    CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });

            MainEvents.Trigger(MainEvents.Names.SubjectFollowChanged, stored);
            return true;
        }

        /// <summary> Stops following a subject. </summary>
        /// <param name="follower"> The unlocked account withdrawing the interest. </param>
        /// <param name="subject"> Subject as it was written. </param>
        /// <returns> A task that completes once the interest is gone. </returns>
        public static async Task UnfollowAsync(PrivateIdentity follower, string subject)
        {
            string stored = Normalise(subject);
            if (stored.Length == 0) return;

            await AppServices.Documents.DeleteAsync(SubjectFollowData.IdFor(follower.Public.Address, stored));
            MainEvents.Trigger(MainEvents.Names.SubjectFollowChanged, stored);
        }

        /// <summary> True when an account already follows a subject. </summary>
        /// <param name="followerAddress"> Address of the reader. </param>
        /// <param name="subject"> Subject as it was written. </param>
        /// <returns> Whether the interest is on record. </returns>
        public static async Task<bool> IsFollowingAsync(string followerAddress, string subject)
        {
            string stored = Normalise(subject);
            if (followerAddress.Length == 0 || stored.Length == 0) return false;

            return await AppServices.Documents.ReadAsync(SubjectFollowData.IdFor(followerAddress, stored)) is not null;
        }

        /// <summary> Reads the subjects one account follows. </summary>
        /// <param name="followerAddress"> Address of the reader. </param>
        /// <param name="limit"> Largest number of subjects to return. </param>
        /// <returns> The subjects, in the form they are stored under. </returns>
        public static async Task<IReadOnlyList<string>> ReadFollowedSubjectsAsync(string followerAddress, int limit = SubjectsPerReader)
        {
            if (followerAddress.Length == 0 || limit <= 0) return [];

            DocumentQuery<SubjectFollowData> query = new DocumentQuery<SubjectFollowData>()
                .WithMatch(SubjectFollowData.FollowerField, followerAddress)
                .WithSort(SubjectFollowData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(follow => follow.Subject)];
        }

        /// <summary>
        /// The form a subject is followed under. Every entry point goes through this, so following "#Tea" from a
        /// post and opening "/subject/tea" from a link are the same interest rather than two.
        /// </summary>
        /// <param name="subject"> Subject as it was written, with or without its mark. </param>
        /// <returns> Its stored form, or empty when there is no subject in the text. </returns>
        static string Normalise(string subject)
        {
            string trimmed = subject.Trim().TrimStart(WrittenText.SubjectMark);

            return trimmed.Length == 0 ? string.Empty : WrittenText.NormaliseSubject(trimmed);
        }
    }
}
