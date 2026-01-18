namespace osync
{
    /// <summary>
    /// Test suite loaded from an external JSON file
    /// </summary>
    public class ExternalTestSuite : ITestSuite
    {
        private readonly ExternalTestSuiteJson _data;

        public ExternalTestSuite(ExternalTestSuiteJson data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public string Name => _data.Name;

        public int TotalQuestions => _data.Categories.Sum(c => c.Questions.Count);

        public int? NumPredict => _data.NumPredict;

        public int ContextLength => _data.ContextLength;

        public List<TestCategory> GetCategories()
        {
            return _data.Categories;
        }
    }
}
