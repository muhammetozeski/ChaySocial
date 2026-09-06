namespace ChaySocial.MainProject.DataModels
{
    /// <summary> Who the writer of a post left the door open to. </summary>
    /// <remarks>
    /// <see cref="Anyone"/> is deliberately zero: it is what every post written before this existed meant, and
    /// what a post still means when its writer says nothing.
    /// </remarks>
    public enum ReplyCircle
    {
        /// <summary> Everybody, which is what a post says when its writer did not narrow it. </summary>
        Anyone,

        /// <summary> Only the accounts its writer follows. </summary>
        FollowedByAuthor,

        /// <summary> Only the accounts its writer named in it. </summary>
        NamedOnly,

        /// <summary> Nobody. The post is something said rather than something asked. </summary>
        NoOne
    }
}
