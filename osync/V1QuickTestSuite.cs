namespace osync
{
    /// <summary>
    /// Quick test suite v1quick with 10 questions (2 per category) for faster testing
    /// </summary>
    public class V1QuickTestSuite : ITestSuite
    {
        public string Name => "v1quick";

        public int TotalQuestions => 10;

        public List<TestCategory> GetCategories()
        {
            return new List<TestCategory>
            {
                CreateReasoningCategory(),
                CreateMathCategory(),
                CreateFinanceCategory(),
                CreateTechnologyCategory(),
                CreateScienceCategory()
            };
        }

        private TestCategory CreateReasoningCategory()
        {
            return new TestCategory
            {
                Id = 1,
                Name = "Reasoning",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 1,
                        Text = "You have a basket containing 5 red apples and 3 green apples. You remove 2 red apples and add 4 yellow bananas. Then you remove 1 green apple. What is the final count of fruits in your basket, and what are their colors? Please explain your reasoning step by step."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 2,
                        Text = "There are three boxes: Box A contains only black marbles, Box B contains only white marbles, and Box C contains a mix of black and white marbles. All three boxes are labeled incorrectly. You can only draw one marble from one box to determine the correct labels for all three boxes. Which box should you choose, and why? Explain your complete reasoning."
                    }
                }
            };
        }

        private TestCategory CreateMathCategory()
        {
            return new TestCategory
            {
                Id = 2,
                Name = "Math",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 1,
                        Text = "Calculate the result of this expression step by step: (15 + 7) × 3 - 18 ÷ 2 + 4². Show all your work and explain each operation clearly according to the order of operations."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 2,
                        Text = "A rectangular garden measures 12 meters in length and 8 meters in width. You want to create a stone path that is 1 meter wide all around the inside perimeter of the garden. What is the area of the remaining garden space after the path is constructed? Show all calculations and explain your method."
                    }
                }
            };
        }

        private TestCategory CreateFinanceCategory()
        {
            return new TestCategory
            {
                Id = 3,
                Name = "Finance",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 1,
                        Text = "You purchase a stock for $50 per share. The stock price increases by 20% in the first year, then decreases by 20% in the second year. What is the final stock price per share, and what is your overall percentage gain or loss? Explain why the percentage changes don't simply cancel out."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 2,
                        Text = "A business takes out a simple interest loan of $10,000 at an annual interest rate of 8% for 3 years. Calculate the total interest paid and the total amount that must be repaid at the end of the loan term. Show all calculations clearly."
                    }
                }
            };
        }

        private TestCategory CreateTechnologyCategory()
        {
            return new TestCategory
            {
                Id = 4,
                Name = "Technology",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 1,
                        Text = "Explain the difference between RAM and ROM in computer systems. Describe their specific purposes, characteristics, and provide practical examples of how each type of memory is used. Include information about volatility and typical sizes."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 2,
                        Text = "What is the difference between HTTP and HTTPS protocols? Explain how HTTPS provides security, what SSL/TLS certificates are, and why it's important for websites to use HTTPS, especially for e-commerce and banking sites."
                    }
                }
            };
        }

        private TestCategory CreateScienceCategory()
        {
            return new TestCategory
            {
                Id = 5,
                Name = "Science",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 1,
                        Text = "Explain the process of photosynthesis in plants, including the light-dependent reactions and the Calvin cycle. Describe the inputs and outputs of each stage, the role of chlorophyll, and why photosynthesis is crucial for life on Earth."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 2,
                        Text = "What is Newton's Second Law of Motion? Explain the relationship between force, mass, and acceleration using the formula F = ma. Provide practical examples demonstrating this law, including calculations showing how changing mass or force affects acceleration."
                    }
                }
            };
        }
    }
}
