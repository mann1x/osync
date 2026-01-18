namespace osync
{
    /// <summary>
    /// Interface for test suites used in quantization comparison
    /// Implement this interface to create custom test suites
    /// </summary>
    public interface ITestSuite
    {
        /// <summary>
        /// Unique name identifying this test suite
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Get all test categories with their questions
        /// </summary>
        /// <returns>List of test categories</returns>
        List<TestCategory> GetCategories();

        /// <summary>
        /// Get total number of questions across all categories
        /// </summary>
        int TotalQuestions { get; }

        /// <summary>
        /// Maximum number of tokens to generate per response
        /// Optional - if null, Ollama uses its default
        /// </summary>
        int? NumPredict { get; }

        /// <summary>
        /// Context length (num_ctx) for the model during testing
        /// Default is 4096 for most test suites
        /// Can be overridden at category or question level
        /// </summary>
        int ContextLength { get; }
    }
}
