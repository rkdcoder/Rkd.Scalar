namespace Rkd.Scalar
{
    /// <summary>
    /// Technical details of an exception that are written to the log and <b>never</b> sent to the client
    /// (ids, SQL error numbers, upstream responses…). Implement it on your own exceptions (for example with
    /// <c>string? IProblemLogDetails.LogDetails =&gt; Details;</c>); <see cref="ProblemException"/> already does.
    /// </summary>
    public interface IProblemLogDetails
    {
        /// <summary>Text added to the log entry of the error response.</summary>
        string? LogDetails { get; }
    }
}
