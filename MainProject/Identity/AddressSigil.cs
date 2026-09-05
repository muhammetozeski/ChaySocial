using ChaySocial.MainProject.Cryptography;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// A small mark drawn from the key fingerprint that already sits inside an address, so an account can be
    /// recognised by something other than forty-one characters of base32 nobody can hold in their head.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is stored and nothing is chosen: the same address draws the same mark on every device, forever,
    /// because it is only a reading of the fingerprint. A display name and a teapot emoji can both be copied in a
    /// second, and the mark beside them cannot be picked at all — it is whatever the keys happened to hash to.
    /// </para>
    /// <para>
    /// What it is NOT: proof of identity. The shape reads only fifteen bits of the fingerprint, so somebody willing
    /// to generate a few tens of thousands of accounts can land on any shape they like, and even by accident two
    /// accounts in four hundred came out with the same shape when this was measured. Colour widens it and does not
    /// change the kind of thing it is. This is a recognition aid against casual copying, in the way a face is: it
    /// makes a stranger wearing somebody's name look wrong at a glance. The address itself remains the only
    /// identity, and anything that has to be certain still checks a signature.
    /// </para>
    /// <para>
    /// The grid is mirrored down its middle. An unmirrored pattern of the same size reads as noise; a symmetric one
    /// reads as a seal, and symmetry is what makes two marks easy to tell apart at a glance.
    /// </para>
    /// </remarks>
    /// <param name="Cells"> The left half plus the middle column, row by row; the right half is these mirrored. </param>
    /// <param name="InkSeed"> Fingerprint byte that picks the mark's main colour. </param>
    /// <param name="AccentSeed"> Fingerprint byte that picks its second colour. </param>
    public sealed record AddressSigil(IReadOnlyList<bool> Cells, byte InkSeed, byte AccentSeed)
    {
        /// <summary> How many cells across the mark is. </summary>
        public const int GridColumnCount = 5;

        /// <summary> How many cells down the mark is. </summary>
        public const int GridRowCount = 5;

        /// <summary> Columns that are actually drawn from the fingerprint; the rest are these reflected. </summary>
        const int MirroredColumnCount = 3;

        /// <summary> Which fingerprint byte picks the main colour. Taken from the tail so it is independent of the cells. </summary>
        const int InkByteIndex = 19;

        /// <summary> Which fingerprint byte picks the second colour. </summary>
        const int AccentByteIndex = 18;

        /// <summary> How many cells the fingerprint decides; the mirror fills the rest. </summary>
        const int IndependentCellCount = MirroredColumnCount * GridRowCount;

        /// <summary> How many bits are in a byte, for reading the cells out of the fingerprint. </summary>
        const int BitsPerByte = 8;

        /// <summary>
        /// Reads the mark an address carries.
        /// </summary>
        /// <param name="address"> The address as it is written. </param>
        /// <returns> Its mark, or null when the text is not a well-formed address of this application. </returns>
        public static AddressSigil? Build(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (!AppCryptography.Addresses.TryGetFingerprint(address, out byte[] fingerprint)) return null;

            bool[] cells = new bool[IndependentCellCount];
            for (int index = 0; index < IndependentCellCount; index++)
            {
                byte source = fingerprint[index / BitsPerByte];
                cells[index] = (source & (1 << (index % BitsPerByte))) != 0;
            }

            return new AddressSigil(cells, fingerprint[InkByteIndex], fingerprint[AccentByteIndex]);
        }

        /// <summary> True when the cell at this position is inked. </summary>
        /// <param name="column"> Column, counted from the left. </param>
        /// <param name="row"> Row, counted from the top. </param>
        /// <returns> Whether to draw this cell. </returns>
        public bool IsLit(int column, int row)
        {
            // Past the middle, look back at the column's reflection rather than at a cell of its own.
            int readColumn = column < MirroredColumnCount ? column : GridColumnCount - 1 - column;

            return Cells[(row * MirroredColumnCount) + readColumn];
        }

        /// <summary> Which of the caller's inks this mark is drawn in. </summary>
        /// <param name="inkCount"> How many inks the caller has to offer. </param>
        /// <returns> An index into them. </returns>
        public int InkIndex(int inkCount) => InkSeed % inkCount;

        /// <summary>
        /// Which ink the mark's second colour uses. Always a different one from <see cref="InkIndex"/>, so a mark
        /// never comes out as a single flat colour.
        /// </summary>
        /// <param name="inkCount"> How many inks the caller has to offer. </param>
        /// <returns> An index into them, never equal to the main one. </returns>
        public int AccentIndex(int inkCount)
            => inkCount <= 1 ? 0 : (InkIndex(inkCount) + 1 + (AccentSeed % (inkCount - 1))) % inkCount;
    }
}
