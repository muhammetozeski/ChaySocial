namespace ChaySocial.MainProject.Services
{
    /// <summary> Turns a timestamp into the short "how long ago" label posts carry, e.g. <c>just now</c>, <c>7m</c>, <c>3h</c>, <c>2d</c>. </summary>
    public static class RelativeTimeFormatter
    {
        /// <summary> Anything younger than this reads as "just now" rather than a count. </summary>
        const int JustNowBelowSeconds = 45;

        const int SecondsPerMinute = 60;
        const int MinutesPerHour = 60;
        const int HoursPerDay = 24;
        const int DaysPerWeek = 7;

        /// <summary> Formats how long ago something happened. </summary>
        /// <param name="unixMilliseconds"> When it happened. </param>
        /// <param name="now"> What to measure against; defaults to now. </param>
        /// <returns> The short label to draw next to the item. </returns>
        public static string Format(long unixMilliseconds, DateTimeOffset? now = null)
        {
            DateTimeOffset moment = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
            TimeSpan elapsed = (now ?? DateTimeOffset.UtcNow) - moment;

            if (elapsed < TimeSpan.Zero) return "just now";
            if (elapsed.TotalSeconds < JustNowBelowSeconds) return "just now";
            if (elapsed.TotalSeconds < SecondsPerMinute * MinutesPerHour) return $"{(int)elapsed.TotalMinutes}m";
            if (elapsed.TotalHours < HoursPerDay) return $"{(int)elapsed.TotalHours}h";
            if (elapsed.TotalDays < DaysPerWeek) return $"{(int)elapsed.TotalDays}d";

            return moment.ToLocalTime().ToString(Constants.ContentConstants.DateFormats.UiBanner);
        }
    }
}
