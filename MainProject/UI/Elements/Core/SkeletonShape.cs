namespace ChaySocial.MainProject.UI.Elements.Core
{
    /// <summary> Which shape a loading placeholder should take, so the wait looks like what is coming. </summary>
    public enum SkeletonShape
    {
        /// <summary> A post-sized card: a face, a heading and a couple of lines. </summary>
        Card,

        /// <summary> A list row: a face and two short lines. </summary>
        Row,

        /// <summary> Lines alone, for a block of text with nobody's face beside it. </summary>
        Lines
    }
}
