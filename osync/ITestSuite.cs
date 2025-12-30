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
    }
}
