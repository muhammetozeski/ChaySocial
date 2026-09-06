namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// The orders a reader can put their own feed in. On every other platform this is a secret the reader is
    /// subject to; here it is a short list they pick from, and the choice never leaves their device.
    /// </summary>
    public enum FeedOrder
    {
        /// <summary> Newest first, which is what the wall has always done and what a reader who never touches this gets. </summary>
        Newest,

        /// <summary> Oldest first, for reading a day forwards instead of backwards. </summary>
        Oldest,

        /// <summary> Least answered first, so a post nobody replied to gets the screen before a loud one's fifth. </summary>
        FewestChaysFirst,

        /// <summary> One post per account before anybody gets a second, so no single account can fill the screen. </summary>
        OneEachTurn,

        /// <summary> Shuffled from a seed the reader can throw again, so the same seed always gives the same page. </summary>
        Shuffled
    }
}
