namespace Rkd.Scalar.OpenApi
{
    public sealed class OpenApiDocumentRegistry
    {
        private readonly HashSet<string> _documents = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string name)
        {
            _documents.Add(name);
        }

        public IReadOnlyCollection<string> GetDocuments()
        {
            return _documents.Count == 0
                ? ["v1"]
                : _documents;
        }
    }
}
