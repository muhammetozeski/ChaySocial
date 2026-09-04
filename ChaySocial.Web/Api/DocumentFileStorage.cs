using System.Text.Json.Nodes;

namespace ChaySocial.Web.Api
{
    /// <summary>
    /// Keeps documents on disk so a restart does not empty the app. One directory per collection, one file per
    /// document: <c>&lt;root&gt;/&lt;collection&gt;/&lt;escaped id&gt;.json</c>. Writes go through a temporary file and a
    /// move, so a process that dies mid-write leaves the previous version intact rather than a half-written one.
    /// </summary>
    /// <param name="rootDirectory"> Directory the collections live under; created when it does not exist. </param>
    public sealed class DocumentFileStorage(string rootDirectory)
    {
        /// <summary> Extension every stored document carries. </summary>
        const string DocumentExtension = ".json";

        /// <summary> Extension of the half-written file a save uses before it is moved into place. </summary>
        const string PendingExtension = ".writing";

        /// <summary> Where the collections live, so a caller can report it at startup. </summary>
        public string RootDirectory => rootDirectory;

        /// <summary>
        /// Reads everything back into memory. Called once at startup; a document that no longer parses is reported
        /// and skipped rather than taking the whole app down with it.
        /// </summary>
        /// <returns> Every stored document with the collection and id it belongs to. </returns>
        public List<(string Collection, string DocumentId, JsonNode Document)> LoadAll()
        {
            List<(string, string, JsonNode)> loaded = [];
            if (!Directory.Exists(rootDirectory)) return loaded;

            foreach (string collectionDirectory in Directory.EnumerateDirectories(rootDirectory))
            {
                string collection = Uri.UnescapeDataString(Path.GetFileName(collectionDirectory));

                foreach (string documentPath in Directory.EnumerateFiles(collectionDirectory, "*" + DocumentExtension))
                {
                    string documentId = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(documentPath));

                    try
                    {
                        JsonNode? document = JsonNode.Parse(File.ReadAllText(documentPath));
                        if (document is not null) loaded.Add((collection, documentId, document));
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine($"Skipping unreadable document '{documentPath}': {error.Message}");
                    }
                }
            }

            return loaded;
        }

        /// <summary> Writes one document, replacing whatever was under that id. </summary>
        /// <param name="collection"> Collection the document belongs to. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        /// <param name="document"> The JSON to store. </param>
        public void Save(string collection, string documentId, JsonNode document)
        {
            string collectionDirectory = Path.Combine(rootDirectory, Uri.EscapeDataString(collection));
            Directory.CreateDirectory(collectionDirectory);

            string documentPath = Path.Combine(collectionDirectory, Uri.EscapeDataString(documentId) + DocumentExtension);
            string pendingPath = documentPath + PendingExtension;

            File.WriteAllText(pendingPath, document.ToJsonString());
            File.Move(pendingPath, documentPath, overwrite: true);
        }

        /// <summary> Removes one document. Removing something that is not there is not an error. </summary>
        /// <param name="collection"> Collection the document belongs to. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        public void Remove(string collection, string documentId)
        {
            string documentPath = Path.Combine(
                rootDirectory,
                Uri.EscapeDataString(collection),
                Uri.EscapeDataString(documentId) + DocumentExtension);

            File.Delete(documentPath);
        }
    }
}
