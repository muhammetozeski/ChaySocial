namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// The words a pair phrase is spoken in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly <see cref="WordListSize"/> of them, which is one whole byte per word. A shorter list would mean
    /// folding a byte into fewer words, and folding leaves some words commoner than others — the phrase would then
    /// carry less than the bits it looks like it carries.
    /// </para>
    /// <para>
    /// Chosen to survive a bad line: one or two syllables, no two that sound alike, and never both halves of a
    /// homophone pair. A phrase is only worth reading out if the person at the other end hears the same words.
    /// </para>
    /// </remarks>
    public static class PairWords
    {
        /// <summary> How many words there are: one per value a byte can take, so no value folds onto another. </summary>
        public const int WordListSize = 256;

        /// <summary> The words, indexed by the byte that picks them. </summary>
        public static readonly IReadOnlyList<string> Spoken =
        [
            "acid", "acorn", "afraid", "agent", "album", "alley", "almond", "amber",
            "anchor", "angle", "ankle", "apple", "apron", "arch", "arctic", "argue",
            "armor", "arrow", "artist", "aspect", "attic", "author", "autumn", "avoid",
            "awake", "axis", "bacon", "badge", "bagel", "baker", "balcony", "bamboo",
            "banjo", "barley", "basket", "batch", "beacon", "beetle", "bench", "berry",
            "bicycle", "bishop", "bitter", "blanket", "blossom", "bonus", "border", "bottle",
            "boulder", "bracket", "branch", "brass", "bridge", "bronze", "brush", "bubble",
            "bucket", "buffalo", "bundle", "burden", "butter", "cabin", "cactus", "camel",
            "canal", "candle", "canvas", "canyon", "carbon", "cargo", "carpet", "cashew",
            "castle", "cattle", "cavern", "cedar", "cellar", "cement", "census", "chalk",
            "chapel", "cheese", "cherry", "chimney", "chisel", "cider", "cinder", "circus",
            "clarinet", "clever", "cliff", "clover", "cobalt", "cocoa", "collar", "column",
            "comet", "compass", "copper", "coral", "cotton", "cousin", "cradle", "crayon",
            "cricket", "crimson", "crystal", "cushion", "cymbal", "dagger", "dahlia", "daisy",
            "dancer", "dawn", "decade", "denim", "desert", "diamond", "dinner", "dolphin",
            "domino", "donkey", "dragon", "drawer", "drift", "drum", "dust", "eagle",
            "east", "echo", "elbow", "elder", "elm", "ember", "empire", "engine",
            "envelope", "equal", "essay", "exile", "fabric", "falcon", "fancy", "farmer",
            "fashion", "feather", "fence", "fern", "ferry", "fiber", "fiddle", "figure",
            "filter", "finger", "flame", "flask", "flock", "flour", "flute", "forest",
            "fortune", "fossil", "fountain", "fox", "frame", "freckle", "frost", "fudge",
            "funnel", "gallery", "garden", "garlic", "gecko", "ginger", "glacier", "glass",
            "glider", "goblet", "golden", "granite", "grape", "gravel", "guitar", "gutter",
            "hammer", "hamster", "harbor", "harvest", "hazel", "helmet", "hermit", "hickory",
            "hollow", "honey", "hornet", "hostel", "hunter", "hurdle", "iceberg", "impulse",
            "indigo", "ink", "insect", "iron", "island", "ivory", "jacket", "jaguar",
            "jasmine", "jelly", "jersey", "jewel", "jigsaw", "jockey", "jungle", "juniper",
            "kayak", "kennel", "kettle", "keyhole", "kingdom", "kitten", "knuckle", "ladder",
            "lagoon", "lantern", "lapel", "lard", "laser", "lattice", "lava", "lawn",
            "leather", "ledger", "lemon", "lentil", "leopard", "lettuce", "lever", "lily",
            "linen", "lizard", "lobster", "locker", "lotus", "lumber", "lunar", "lyric",
            "magnet", "mahogany", "mammoth", "mango", "maple", "marble", "marigold", "marsh"
        ];
    }
}
