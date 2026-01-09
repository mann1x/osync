namespace osync
{
    /// <summary>
    /// Default hardcoded test suite v1base with 50 questions across 5 categories
    /// </summary>
    public class V1BaseTestSuite : ITestSuite
    {
        public string Name => "v1base";

        public int TotalQuestions => 50;

        public int NumPredict => 4096;

        public int ContextLength => 4096;

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
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 3,
                        Text = "A farmer has 17 sheep. All but 9 die. How many sheep does the farmer have left? Explain your answer and the reasoning behind it, considering the wording of the question carefully."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 4,
                        Text = "If you're running a race and you pass the person in second place, what position are you in now? Explain your reasoning thoroughly, considering the implications of passing someone in second place."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 5,
                        Text = "A mother is 21 years older than her child. In exactly 6 years from now, the child will be exactly 5 times younger than the mother. Where is the father right now? Think through this problem carefully and explain your reasoning step by step."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 6,
                        Text = "You have two ropes, each of which takes exactly one hour to burn completely. The ropes burn unevenly, meaning some parts burn faster than others. Using only these two ropes and matches to light them, how can you measure exactly 45 minutes? Explain your method in detail."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 7,
                        Text = "Three switches outside a closed room control three light bulbs inside the room. You can flip the switches however you want, but you can only enter the room once to check the bulbs. How can you definitively determine which switch controls which bulb? Provide a complete explanation of your strategy."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 8,
                        Text = "A man is looking at a photograph. Someone asks who is in the photo, and the man replies: 'Brothers and sisters have I none, but that man's father is my father's son.' Who is in the photograph? Explain your reasoning clearly and completely."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 9,
                        Text = "You are in a room with three doors. One door leads to freedom, one door leads to a room full of fire, and one door leads to a room with a hungry lion that hasn't eaten in three months. Which door is the safest choice and why? Explain your reasoning thoroughly."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 10,
                        Text = "A bat and a ball together cost $1.10. The bat costs $1.00 more than the ball. How much does the ball cost? Show your work and explain why the intuitive answer is incorrect."
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
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 3,
                        Text = "A train travels at an average speed of 80 kilometers per hour for the first 2 hours of its journey, then increases its speed to 100 kilometers per hour for the next 3 hours. Calculate the total distance traveled and the overall average speed for the entire journey. Show all steps in your calculations."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 4,
                        Text = "If you invest $1,000 at an annual compound interest rate of 5% compounded annually, how much money will you have after 3 years? Show the calculation for each year and explain the concept of compound interest in your answer."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 5,
                        Text = "A cylinder has a radius of 5 centimeters and a height of 10 centimeters. Calculate both the surface area and the volume of this cylinder. Use π ≈ 3.14159 and show all steps in your calculations with proper units."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 6,
                        Text = "Three numbers are in the ratio 2:3:5. If the sum of these three numbers is 200, what is the value of each individual number? Show your complete solution process step by step."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 7,
                        Text = "A store is offering a 20% discount on all items, and then applies an additional 15% discount on the already reduced price. What is the total effective percentage discount from the original price? Explain why this is not simply 35% off."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 8,
                        Text = "If x + y = 12 and x - y = 4, what are the values of x and y? Show your complete solution using either substitution or elimination method, and verify your answer."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 9,
                        Text = "A water tank can be filled by pipe A in 4 hours and by pipe B in 6 hours. If both pipes are opened together, how long will it take to fill the empty tank completely? Show all steps in your calculation and explain your method."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 10,
                        Text = "Calculate the sum of all even numbers from 2 to 100 inclusive. Show your method and explain whether you used a formula or calculated it directly. If using a formula, explain the formula."
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
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 3,
                        Text = "You are comparing two investment options: Option A offers 6% annual return compounded monthly, while Option B offers 6.2% annual return compounded annually. Which option yields more money after one year on a $5,000 investment? Calculate both final amounts and explain which is better."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 4,
                        Text = "A company's revenue is $500,000, with costs of goods sold at $200,000, operating expenses at $150,000, and taxes at 20% of profit. Calculate the company's gross profit, net profit before taxes, net profit after taxes, and profit margin. Show all calculations with proper labels."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 5,
                        Text = "You want to save $20,000 for a down payment on a house in 5 years. If you can invest money at an annual compound interest rate of 4%, how much do you need to invest today as a lump sum? Show your calculation and explain the concept of present value."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 6,
                        Text = "A credit card has an annual percentage rate (APR) of 18% compounded monthly. You have a balance of $2,000 and make no additional charges or payments for 6 months. Calculate the balance after 6 months, showing the calculation for each month's interest."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 7,
                        Text = "An asset costs $100,000 and depreciates using straight-line depreciation over 10 years with a salvage value of $10,000. Calculate the annual depreciation expense and the book value of the asset after 4 years. Explain the straight-line depreciation method."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 8,
                        Text = "You are evaluating a project that requires an initial investment of $50,000 and will generate cash flows of $15,000 per year for 5 years. If your required rate of return is 8%, calculate the Net Present Value (NPV) of this project. Show the calculation for each year's discounted cash flow."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 9,
                        Text = "A bond with a face value of $1,000 pays a 5% annual coupon rate (paid semi-annually) and matures in 3 years. If the current market interest rate is 6% annually, calculate the fair price of this bond. Explain the relationship between bond prices and interest rates."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 10,
                        Text = "You have a choice between receiving $10,000 today or $12,000 in 2 years. If your discount rate is 7% per year, which option is financially better? Calculate the present value of the future payment and explain your reasoning."
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
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 3,
                        Text = "Describe how a CPU processes instructions using the fetch-decode-execute cycle. Explain each stage in detail and discuss how clock speed affects this process. Also mention what determines overall CPU performance beyond just clock speed."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 4,
                        Text = "Explain the concept of cloud computing, including the three main service models: IaaS, PaaS, and SaaS. Provide specific real-world examples of each service model and explain when a business might choose one model over another."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 5,
                        Text = "What is the difference between machine learning and deep learning? Explain neural networks, how they relate to deep learning, and provide examples of practical applications where deep learning has shown superior performance compared to traditional machine learning."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 6,
                        Text = "Describe how a relational database differs from a NoSQL database. Explain the concepts of tables, schemas, and SQL queries for relational databases, and contrast this with document stores, key-value stores, and the CAP theorem for distributed databases."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 7,
                        Text = "Explain what an API (Application Programming Interface) is and why APIs are important in modern software development. Describe the difference between REST and GraphQL APIs, and provide examples of how applications use APIs to communicate with each other."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 8,
                        Text = "What is blockchain technology and how does it work? Explain the concepts of blocks, chains, distributed ledgers, consensus mechanisms, and mining. Provide examples of blockchain applications beyond cryptocurrency, such as supply chain management or digital identity."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 9,
                        Text = "Describe the OSI model's seven layers and explain the purpose of each layer in network communication. Focus particularly on the application layer, transport layer, and network layer, providing examples of protocols that operate at each level."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 10,
                        Text = "Explain the difference between synchronous and asynchronous programming. Describe scenarios where asynchronous programming is beneficial, how callbacks and promises work, and provide examples of operations that benefit from asynchronous execution such as file I/O or network requests."
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
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 3,
                        Text = "Describe the water cycle on Earth, including evaporation, condensation, precipitation, and collection. Explain the role of the sun in driving this cycle, how temperature affects each stage, and why the water cycle is essential for maintaining Earth's ecosystems."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 4,
                        Text = "Explain the structure of DNA and how genetic information is stored and replicated. Describe the roles of the four nucleotide bases (adenine, thymine, guanine, cytosine), base pairing rules, and the double helix structure. Include information about how DNA codes for proteins."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 5,
                        Text = "What is the greenhouse effect and how does it relate to climate change? Explain which gases are greenhouse gases, how they trap heat in Earth's atmosphere, and the difference between the natural greenhouse effect and enhanced greenhouse effect caused by human activities."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 6,
                        Text = "Describe the three states of matter (solid, liquid, gas) in terms of particle arrangement and movement. Explain what happens during phase transitions between these states, using ice melting to water and water evaporating to steam as examples. Include discussion of energy changes during these transitions."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 7,
                        Text = "Explain how vaccines work to provide immunity against diseases. Describe the role of the immune system, antibodies, and memory cells. Include information about different types of vaccines (live attenuated, inactivated, mRNA) and why vaccines are important for public health."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 8,
                        Text = "What is the theory of evolution by natural selection as proposed by Charles Darwin? Explain the key concepts including variation, inheritance, selection pressure, and adaptation. Provide specific examples such as antibiotic resistance in bacteria or Darwin's finches in the Galápagos Islands."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 9,
                        Text = "Describe the structure and function of the human circulatory system. Explain the role of the heart, arteries, veins, and capillaries. Include information about how blood carries oxygen and nutrients to cells, the difference between oxygenated and deoxygenated blood, and the path blood takes through the body."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 10,
                        Text = "Explain the concept of pH and the pH scale. Describe what makes a solution acidic, neutral, or basic, and provide examples of common substances at different pH levels. Include information about acids, bases, and why pH is important in biological systems such as human blood."
                    }
                }
            };
        }
    }
}
