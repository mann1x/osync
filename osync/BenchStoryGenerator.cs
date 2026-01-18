using System.Text;
using System.Text.Json;

namespace osync
{
    /// <summary>
    /// Generates story content and questions for benchmark test suites.
    /// Creates unique, deterministic content with trackable facts.
    /// </summary>
    public class BenchStoryGenerator
    {
        private readonly Random _random;
        private readonly HashSet<string> _usedFacts = new();
        private readonly HashSet<string> _usedQuestions = new();
        private int _chapterNumber = 0;
        private readonly int? _contextPercentOverride;

        // Data pools for story generation
        private static readonly string[] Locations = {
            "Forest City", "Mountain Village", "River Town", "Desert Oasis", "Crystal Lake",
            "Coastal Haven", "Meadow Valley", "Pine Ridge", "Sunset Plains", "Starlight Grove",
            "Bamboo Garden", "Cherry Blossom Hill", "Golden Dunes", "Silver Falls", "Emerald Forest",
            "Sapphire Bay", "Ruby Canyon", "Amber Fields", "Ivory Tower", "Jade Temple",
            "Obsidian Peak", "Coral Reef", "Thunder Mountains", "Whisper Woods", "Harvest Hollow",
            "Moonlit Marsh", "Sunfire Desert", "Frostbite Tundra", "Rainbow Falls", "Twilight Vale",
            "Blossom Village", "Autumn Ridge", "Winter Haven", "Summer Isle", "Spring Meadow",
            "Copper Canyon", "Iron Fortress", "Bronze Beach", "Platinum Peaks", "Diamond Lake",
            "Opal Springs", "Topaz Terrace", "Amethyst Cave", "Pearl Harbor", "Turquoise Lagoon",
            "Garnet Gorge", "Onyx Cavern", "Citrine City", "Aquamarine Atoll", "Peridot Plains"
        };

        private static readonly string[] Animals = {
            "Tiger", "Turtle", "Gorilla", "Eagle", "Dolphin", "Elephant", "Fox", "Bear",
            "Wolf", "Owl", "Rabbit", "Deer", "Lion", "Leopard", "Cheetah", "Panther",
            "Horse", "Donkey", "Zebra", "Giraffe", "Hippo", "Rhino", "Crocodile", "Parrot",
            "Penguin", "Flamingo", "Pelican", "Swan", "Beaver", "Otter", "Seal", "Whale",
            "Shark", "Octopus", "Crab", "Lobster", "Peacock", "Crane", "Heron", "Falcon"
        };

        private static readonly string[] Names = {
            "Marcus", "Luna", "Bud", "Sage", "Rocky", "Coral", "Storm", "River",
            "Willow", "Oak", "Maple", "Cedar", "Jasper", "Ruby", "Pearl", "Jade",
            "Amber", "Ivy", "Fern", "Moss", "Brook", "Lake", "Cliff", "Ridge",
            "Vale", "Dale", "Glen", "Heath", "Marsh", "Dune", "Stone", "Flint",
            "Ash", "Birch", "Pine", "Holly", "Hazel", "Laurel", "Rose", "Lily",
            "Daisy", "Violet", "Iris", "Orchid", "Lotus", "Clover", "Basil", "Mint",
            "Ginger", "Pepper", "Cinnamon", "Saffron", "Nutmeg", "Cardamom", "Fennel", "Thyme",
            "Aurora", "Nova", "Stella", "Luna", "Sol", "Terra", "Marina", "Sierra",
            "Dakota", "Phoenix", "Griffin", "Drake", "Raven", "Falcon", "Hawk", "Sparrow",
            "Wren", "Robin", "Jay", "Finch", "Lark", "Dove", "Swan", "Crane"
        };

        private static readonly string[] Items = {
            "apples", "bananas", "oranges", "strawberries", "cherries", "blueberries",
            "peaches", "pears", "mangoes", "grapes", "raspberries", "blackberries",
            "books", "scrolls", "paintings", "sculptures", "gems", "crystals",
            "flowers", "seeds", "herbs", "spices", "fabrics", "silks",
            "pottery", "jewelry", "coins", "pearls", "shells", "feathers",
            "honey jars", "maple syrup bottles", "olive oil flasks", "wine bottles",
            "cheese wheels", "bread loaves", "pastries", "cakes", "pies", "cookies",
            "candles", "lanterns", "blankets", "pillows", "rugs", "tapestries",
            "musical instruments", "carved figurines", "woven baskets", "clay pots"
        };

        private static readonly string[] Actions = {
            "gifted", "brought", "delivered", "presented", "offered", "shared",
            "donated", "contributed", "supplied", "provided", "sent", "gave"
        };

        private static readonly string[] Terrains = {
            "dense forest", "rocky mountains", "flowing river", "sandy desert",
            "grassy meadow", "snowy tundra", "tropical jungle", "misty swamp",
            "rolling hills", "steep cliffs", "peaceful valley", "ancient ruins"
        };

        private static readonly string[] Emotions = {
            "delighted", "overjoyed", "grateful", "thankful", "pleased", "happy",
            "excited", "thrilled", "touched", "moved", "honored", "blessed"
        };

        private static readonly string[] Foods = {
            "fruit salad", "vegetable soup", "honey cake", "berry pie", "roasted nuts",
            "fresh bread", "herbal tea", "sweet pudding", "spiced rice", "grilled fish"
        };

        private static readonly string[] Topics = {
            "ancient legends", "upcoming festivals", "trade routes", "weather patterns",
            "local traditions", "farming techniques", "artistic crafts", "healing herbs",
            "star constellations", "historical events", "musical compositions", "poetry"
        };

        private static readonly string[] GroupActions = {
            "explore the nearby caves", "visit the ancient temple", "attend the harvest festival",
            "organize a community feast", "plant new trees", "build a bridge",
            "create a mural", "compose a song", "write a story", "host a celebration"
        };

        /// <summary>
        /// Create a new story generator with the given seed and optional content scaling percentage.
        /// </summary>
        /// <param name="seed">Random seed for reproducibility</param>
        /// <param name="contentScalePercent">Content scaling percentage (50-150). 100 = calibrated default, 80 = 80% of content, 120 = 120% of content. Null = 100%.</param>
        public BenchStoryGenerator(int seed = 42, int? contentScalePercent = null)
        {
            _random = new Random(seed);
            if (contentScalePercent.HasValue)
            {
                // Clamp to valid range (50-150%)
                _contextPercentOverride = Math.Clamp(contentScalePercent.Value, 50, 150);
            }
        }

        /// <summary>
        /// Generate a complete test suite for ctxbench (no tools)
        /// </summary>
        public BenchTestSuite GenerateCtxBenchSuite()
        {
            _chapterNumber = 0;
            _usedFacts.Clear();
            _usedQuestions.Clear();
            _generatedContentChars.Clear();
            _usedInCurrentChapter.Clear();
            _factsByCategory.Clear();
            ResetGlobalTracking();

            var suite = new BenchTestSuite
            {
                TestType = "ctxbench",
                TestDescription = "Context length testing - evaluates model ability to recall information from varying context sizes",
                MaxContextLength = 262144,
                JudgeRequired = true,
                JudgePrompt = GetDefaultJudgePrompt(),
                JudgeSystemPrompt = GetDefaultJudgeSystemPrompt(),
                ToolsEnabled = false,
                Instructions = GetDefaultInstructions(),
                ContextLengthOverhead = 2048,
                ContextLengthOverheadThinking = 4096,
                Categories = new List<BenchCategory>()
            };

            // Generate categories: 2k, 4k, 8k, 12k, 16k, 32k, 64k, 128k, 256k
            var categorySizes = new[] { 2048, 4096, 8192, 12288, 16384, 32768, 65536, 131072, 262144 };
            var categoryNames = new[] { "2k", "4k", "8k", "12k", "16k", "32k", "64k", "128k", "256k" };

            for (int i = 0; i < categorySizes.Length; i++)
            {
                var category = GenerateCategory(categoryNames[i], categorySizes[i], i == 0, false, categoryNames.Take(i).ToList());
                suite.Categories.Add(category);
            }

            return suite;
        }

        /// <summary>
        /// Generate a complete test suite for ctxtoolsbench (with tools)
        /// </summary>
        public BenchTestSuite GenerateCtxToolsBenchSuite()
        {
            _chapterNumber = 0;
            _usedFacts.Clear();
            _usedQuestions.Clear();
            _generatedContentChars.Clear();
            _usedInCurrentChapter.Clear();
            _factsByCategory.Clear();
            ResetGlobalTracking();

            var suite = new BenchTestSuite
            {
                TestType = "ctxtoolsbench",
                TestDescription = "Context length testing with tools - evaluates model ability to recall information and use tools correctly",
                MaxContextLength = 262144,
                JudgeRequired = true,
                JudgePrompt = GetDefaultJudgePrompt(),
                JudgeSystemPrompt = GetDefaultJudgeSystemPrompt(),
                ToolsEnabled = true,
                EnabledTools = null, // All tools enabled
                Instructions = GetToolsInstructions(),
                ContextLengthOverhead = 2048,
                ContextLengthOverheadThinking = 4096,
                Categories = new List<BenchCategory>()
            };

            // Generate categories: 2k, 4k, 8k, 12k, 16k, 32k, 64k, 128k, 256k
            var categorySizes = new[] { 2048, 4096, 8192, 12288, 16384, 32768, 65536, 131072, 262144 };
            var categoryNames = new[] { "2k", "4k", "8k", "12k", "16k", "32k", "64k", "128k", "256k" };

            for (int i = 0; i < categorySizes.Length; i++)
            {
                var category = GenerateCategory(categoryNames[i], categorySizes[i], i == 0, true, categoryNames.Take(i).ToList());
                suite.Categories.Add(category);
            }

            return suite;
        }

        // Track actual generated content sizes for accurate cumulative calculation
        private readonly Dictionary<string, int> _generatedContentChars = new();

        private BenchCategory GenerateCategory(string name, int contextLength, bool isFirst, bool useTools, List<string> previousCategories)
        {
            // Calibrated target usage percent: 90% for small contexts (2k/4k), 95% for larger
            // These are the calibrated defaults that represent 100% content scaling
            double targetUsagePercent = contextLength <= 4096 ? 0.90 : 0.95;

            // Content scaling factor: 100 = calibrated default, 80 = 80% of content, 120 = 120%
            double contentScaleFactor = _contextPercentOverride.HasValue
                ? _contextPercentOverride.Value / 100.0
                : 1.0;

            // Use BenchTokenizer's default chars per token ratio (llama3.2 style)
            double charsPerToken = BenchTokenizer.DefaultCharsPerToken;

            // Question counts: New always has 10 questions, Old has 1 per previous category
            const int newQuestions = 10;
            int oldQuestions = previousCategories.Count; // 1 question per previous category
            int numQuestions = isFirst ? newQuestions : (oldQuestions + newQuestions);

            // IMPROVED OVERHEAD CALCULATION:
            // Estimate Q&A tokens based on actual expected lengths
            //
            // Per question breakdown (with --nothinking):
            // - Question text: ~20 tokens
            // - Reference answer: ~8 tokens (short factual answers)
            // - Model response: ~150 tokens (model is verbose even without thinking)
            // - Framing/context: ~30 tokens
            // Total per Q&A: ~208 tokens
            //
            // Model acknowledgment (first response): ~250 tokens

            // Per-question overhead: estimated tokens per Q&A pair
            // Actual test: 2k with 10 Q = 2527 tokens, content ~270 tokens = 225 tokens per Q&A
            // Validation estimate is rough - actual usage varies with model and response length
            int modelAckTokens = contextLength switch
            {
                <= 4096 => 100,   // Small contexts: minimal extra overhead
                <= 65536 => 80,   // Medium contexts: standard overhead
                _ => 0            // 128k+: no extra overhead
            };
            int perQuestionTokens = contextLength switch
            {
                <= 2048 => 200,   // 2k: ~200 tokens per Q&A pair
                <= 4096 => 180,   // 4k: slightly less due to efficiency
                <= 8192 => 160,   // 8k: standard Q&A overhead
                <= 16384 => 140,  // 12k-16k: moderate overhead
                <= 32768 => 120,  // 32k: reduced overhead per question
                <= 65536 => 100,  // 64k: minimal overhead per question
                _ => 80           // 128k+: minimal overhead
            };
            int qaOverhead = modelAckTokens + (numQuestions * perQuestionTokens);

            // Calculate content target:
            // Available = contextLength - qaOverhead
            // ContentTarget = Available * targetPercent
            int availableTokens = contextLength - qaOverhead;
            int targetContentTokens = (int)(availableTokens * targetUsagePercent);

            // Previous content tokens (story only - tracked from actual generation)
            int previousContentTokens = 0;
            foreach (var cat in previousCategories)
            {
                if (_generatedContentChars.TryGetValue(cat, out var chars))
                    previousContentTokens += (int)(chars / charsPerToken);
            }

            // Previous categories' Q&A tokens (accumulated in context)
            // Each previous category has: 10 New questions + 1 Old question per its previous categories
            int previousQATokens = 0;
            for (int i = 0; i < previousCategories.Count; i++)
            {
                // Category i has: 10 New questions + i Old questions (1 per previous category before it)
                int prevQCount = (i == 0) ? 10 : (10 + i); // First has 10, second has 11, third has 12, etc.
                previousQATokens += modelAckTokens + prevQCount * perQuestionTokens;
            }

            // New content tokens needed
            // Total available = contextLength - qaOverhead - previousQATokens
            // Target = (Total available - previousContentTokens) * targetPercent
            int totalAvailable = contextLength - qaOverhead - previousQATokens;
            int newContentTokens = (int)((totalAvailable - previousContentTokens) * targetUsagePercent);

            // Convert to chars
            int newContentChars = (int)(newContentTokens * charsPerToken);

            // Apply content scaling factor (100% = calibrated, 80% = less, 120% = more)
            newContentChars = (int)(newContentChars * contentScaleFactor);

            // Minimum content
            newContentChars = Math.Max(newContentChars, 200);

            // Generate the content
            var content = GenerateChapterContent(newContentChars, name);

            // Track actual generated size
            _generatedContentChars[name] = content.Length;

            var category = new BenchCategory
            {
                Name = name,
                ContextLength = contextLength,
                Context = content
            };

            if (isFirst)
            {
                // First category: just "New" subcategory with 10 questions
                category.SubCategories = new List<BenchSubCategory>
                {
                    GenerateNewSubCategory(category.Context, name, useTools, newQuestions)
                };
            }
            else
            {
                // Other categories: "Old" (1 per previous category) + "New" (10 questions)
                category.SubCategories = new List<BenchSubCategory>
                {
                    GenerateOldSubCategory(previousCategories, useTools, oldQuestions),
                    GenerateNewSubCategory(category.Context, name, useTools, newQuestions)
                };
            }

            return category;
        }

        private int GetCategoryContextLength(string categoryName)
        {
            return categoryName switch
            {
                "2k" => 2048,
                "4k" => 4096,
                "8k" => 8192,
                "12k" => 12288,
                "16k" => 16384,
                "32k" => 32768,
                "64k" => 65536,
                "128k" => 131072,
                "256k" => 262144,
                _ => 4096
            };
        }

        private int GetPreviousCategoriesCharCount(List<string> previousCategories)
        {
            // Estimate based on category sizes using 3.0 chars per token
            // Each category contributes its story context chars to the accumulated prompt
            double charsPerToken = 3.0;
            int total = 0;
            foreach (var cat in previousCategories)
            {
                // Estimate chars = context_tokens * chars_per_token - Q&A buffer
                // But we need actual content chars, so use conservative multiplier
                total += cat switch
                {
                    "2k" => (int)(2048 * charsPerToken * 0.7),     // ~4300 chars
                    "4k" => (int)(4096 * charsPerToken * 0.7),     // ~8600 chars
                    "8k" => (int)(8192 * charsPerToken * 0.7),     // ~17200 chars
                    "12k" => (int)(12288 * charsPerToken * 0.7),   // ~25800 chars
                    "16k" => (int)(16384 * charsPerToken * 0.7),   // ~34400 chars
                    "32k" => (int)(32768 * charsPerToken * 0.7),   // ~68800 chars
                    "64k" => (int)(65536 * charsPerToken * 0.7),   // ~137600 chars
                    "128k" => (int)(131072 * charsPerToken * 0.7), // ~275200 chars
                    _ => 0
                };
            }
            return total;
        }

        private BenchSubCategory GenerateOldSubCategory(List<string> previousCategories, bool useTools, int totalQuestions)
        {
            var subCategory = new BenchSubCategory
            {
                Name = "Old",
                AboutCategories = previousCategories.ToList(),
                Questions = new List<BenchQuestion>()
            };

            // Generate exactly 1 question per previous category
            int questionId = 1;
            foreach (var prevCat in previousCategories)
            {
                var questions = GenerateOldQuestions(prevCat, 1, questionId, useTools);
                subCategory.Questions.AddRange(questions);
                questionId++;
            }

            return subCategory;
        }

        private BenchSubCategory GenerateNewSubCategory(string context, string categoryName, bool useTools, int numQuestions = 10)
        {
            return new BenchSubCategory
            {
                Name = "New",
                Questions = GenerateQuestions(context, numQuestions, categoryName, useTools, null)
            };
        }

        private string GenerateChapterContent(int targetChars, string categoryName)
        {
            var sb = new StringBuilder();
            int chaptersNeeded = Math.Max(1, targetChars / 800); // ~800 chars per chapter

            for (int i = 0; i < chaptersNeeded && sb.Length < targetChars; i++)
            {
                _chapterNumber++;
                var chapter = GenerateUniqueChapter(_chapterNumber, categoryName);
                sb.AppendLine(chapter);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private string GenerateUniqueChapter(int chapterNum, string categoryName)
        {
            // Reset per-chapter character tracking (allows reusing characters across chapters, not within same chapter)
            ResetChapterCharacterTracking();

            var sb = new StringBuilder();
            sb.AppendLine($"--- CHAPTER {chapterNum}: {GetRandomElement(Locations).ToUpper()} ADVENTURES ---");
            sb.AppendLine();

            // Generate 3-5 unique events per chapter
            int eventCount = _random.Next(3, 6);
            for (int i = 0; i < eventCount; i++)
            {
                var (character1, animal1) = GetUniqueCharacter();
                var (character2, animal2) = GetUniqueCharacter();
                // Use unique locations, items, quantities across all chapters
                var fromLocation = GetUniqueLocation();
                var toLocation = GetUniqueLocation();
                var action = GetRandomElement(Actions);
                var quantity = GetUniqueQuantity();
                var item = GetUniqueItem();
                var terrain = GetRandomElement(Terrains);
                var duration = _random.Next(1, 15);

                // Create unique fact key
                var factKey = $"{chapterNum}:{character1}:{action}:{quantity}:{item}:{character2}";
                _usedFacts.Add(factKey);

                // Store fact for later question generation
                StoreFact(categoryName, chapterNum, character1, animal1, character2, animal2, action, quantity, item, fromLocation, toLocation, terrain, duration);

                sb.AppendLine($"{character1} the {animal1} traveled from {fromLocation} to {toLocation}.");
                sb.AppendLine($"The journey took {duration} days through the {terrain}.");
                sb.AppendLine($"{character1} the {animal1} {action} {quantity} {item} to {character2} the {animal2}.");

                // Add additional detail
                var emotion = GetRandomElement(Emotions);
                var food = GetRandomElement(Foods);
                sb.AppendLine($"{character2} the {animal2} was {emotion} and prepared {food} to celebrate.");

                var topic = GetRandomElement(Topics);
                var groupAction = GetRandomElement(GroupActions);
                sb.AppendLine($"They discussed {topic} and decided to {groupAction} together.");
                sb.AppendLine();
            }

            sb.AppendLine($"--- END OF CHAPTER {chapterNum} ---");

            return sb.ToString().TrimEnd();
        }

        private readonly Dictionary<string, List<FactInfo>> _factsByCategory = new();

        private void StoreFact(string category, int chapter, string char1, string animal1, string char2, string animal2,
            string action, int quantity, string item, string fromLoc, string toLoc, string terrain, int duration)
        {
            if (!_factsByCategory.ContainsKey(category))
                _factsByCategory[category] = new List<FactInfo>();

            _factsByCategory[category].Add(new FactInfo
            {
                Chapter = chapter,
                Character1 = char1,
                Animal1 = animal1,
                Character2 = char2,
                Animal2 = animal2,
                Action = action,
                Quantity = quantity,
                Item = item,
                FromLocation = fromLoc,
                ToLocation = toLoc,
                Terrain = terrain,
                Duration = duration
            });
        }

        private List<BenchQuestion> GenerateQuestions(string context, int count, string categoryName, bool useTools, string? aboutCategory)
        {
            var questions = new List<BenchQuestion>();
            var facts = _factsByCategory.GetValueOrDefault(categoryName) ?? new List<FactInfo>();

            if (facts.Count == 0) return questions;

            // Generate exactly 'count' questions by cycling through facts
            // Use different question types for each pass through the facts
            for (int i = 0; i < count; i++)
            {
                // Cycle through facts: fact index = i mod facts.Count
                var factIndex = i % facts.Count;
                var fact = facts[factIndex];

                BenchQuestion question;
                if (useTools)
                {
                    question = GenerateToolQuestion(i + 1, fact, aboutCategory);
                }
                else
                {
                    question = GenerateSimpleQuestion(i + 1, fact, aboutCategory);
                }

                // Ensure question is unique by trying different facts/types if needed
                var questionKey = question.Text.ToLowerInvariant();
                int attempts = 0;
                while (_usedQuestions.Contains(questionKey) && attempts < 100)
                {
                    // Try a different fact on collision
                    var altFactIndex = (factIndex + attempts + 1) % facts.Count;
                    var altFact = facts[altFactIndex];
                    if (useTools)
                        question = GenerateToolQuestion(i + 1, altFact, aboutCategory);
                    else
                        question = GenerateSimpleQuestion(i + 1, altFact, aboutCategory);
                    questionKey = question.Text.ToLowerInvariant();
                    attempts++;
                }
                _usedQuestions.Add(questionKey);

                questions.Add(question);
            }

            return questions;
        }

        private List<BenchQuestion> GenerateOldQuestions(string categoryName, int count, int startId, bool useTools)
        {
            var questions = new List<BenchQuestion>();
            var facts = _factsByCategory.GetValueOrDefault(categoryName) ?? new List<FactInfo>();

            if (facts.Count == 0) return questions;

            // Pick facts from different parts of the category
            var indices = new[] { 0, Math.Max(0, facts.Count / 2) };

            for (int i = 0; i < count && i < indices.Length; i++)
            {
                var fact = facts[Math.Min(indices[i], facts.Count - 1)];

                BenchQuestion question;
                if (useTools)
                {
                    question = GenerateToolQuestion(startId + i, fact, categoryName);
                }
                else
                {
                    question = GenerateSimpleQuestion(startId + i, fact, categoryName);
                }
                question.AboutCategory = categoryName;

                questions.Add(question);
            }

            return questions;
        }

        private BenchQuestion GenerateSimpleQuestion(int id, FactInfo fact, string? aboutCategory)
        {
            // Quiz-style questions that test recall from the chapters
            var questionTypes = new[]
            {
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} gave {fact.Item} to someone. How many {fact.Item} were given? Answer with the number only.", fact.Quantity.ToString()),
                () => ($"Quiz: In Chapter {fact.Chapter}, find the animal type of {fact.Character1}. What type of animal is {fact.Character1}?", fact.Animal1),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} traveled between two cities. Which city did they travel FROM?", fact.FromLocation),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} traveled between two cities. Which city did they travel TO?", fact.ToLocation),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} made a journey. How many days did the journey take?", fact.Duration.ToString()),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} traveled through a specific terrain. What type of terrain was it?", fact.Terrain),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} brought items to {fact.Character2}. What type of items were brought?", fact.Item),
                () => ($"Quiz: In Chapter {fact.Chapter}, someone received {fact.Item} from {fact.Character1}. Who received them? Answer with name and animal type.", $"{fact.Character2} the {fact.Animal2}"),
                () => ($"Quiz: In Chapter {fact.Chapter}, find the animal type of {fact.Character2}. What type of animal is {fact.Character2}?", fact.Animal2),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} performed an action involving {fact.Item}. What action verb describes what they did?", fact.Action)
            };

            var (text, answer) = questionTypes[_random.Next(questionTypes.Length)]();

            return new BenchQuestion
            {
                Id = id,
                Text = text,
                ReferenceAnswer = answer,
                AboutCategory = aboutCategory
            };
        }

        private BenchQuestion GenerateToolQuestion(int id, FactInfo fact, string? aboutCategory)
        {
            // Generate quiz-style questions that require CONTEXT RETRIEVAL + tool usage
            // Questions guide the model to: 1) Find info from chapter 2) Use appropriate tool
            var toolQuestionTypes = new List<Func<(string text, string answer, List<string> tools)>>
            {
                // Magic calculator questions - model must find the quantity from context
                () => {
                    var extra = _random.Next(5, 50);
                    var result = (fact.Quantity + extra) + 1; // magic add
                    return ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} gave {fact.Item} to {fact.Character2}. Find how many {fact.Item} were given, then use the magic calculator to add {extra} to that amount. What is the magic result?",
                        result.ToString(), new List<string> { "magic_calculator" });
                },
                () => {
                    var multiplier = _random.Next(2, 5);
                    var result = (fact.Quantity * multiplier) + 1; // magic multiply
                    return ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} brought {fact.Item}. Find the exact quantity from the chapter, then use the magic calculator to multiply it by {multiplier}. What is the magic result?",
                        result.ToString(), new List<string> { "magic_calculator" });
                },

                // get_weather questions - model must find the destination city from context
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} traveled to a new city. Find which city they traveled TO from the chapter, then use the weather tool to get the current temperature there.",
                    GetWeatherTemp(fact.ToLocation), new List<string> { "get_weather" }),

                // get_distance questions - model must find both cities from context
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} traveled between two cities. Find both the origin and destination cities from the chapter, then use the distance tool to calculate the distance between them.",
                    GetDistance(fact.FromLocation, fact.ToLocation), new List<string> { "get_distance" }),

                // get_travel_time questions - model must find both cities from context
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} made a journey. Find the two cities involved from the chapter, then use the travel time tool to calculate how long this journey would take by horse.",
                    GetTravelTime(fact.FromLocation, fact.ToLocation, "horse"), new List<string> { "get_travel_time" }),

                // get_population questions - model must find the destination city from context
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} arrived at a city. Find which city they arrived at from the chapter, then use the population tool to get its population.",
                    GetPopulation(fact.ToLocation), new List<string> { "get_population" }),

                // get_animal_lifespan questions - model must find the animal type from context
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} met {fact.Character2}. Find what type of animal {fact.Character1} is from the chapter (look for \"{fact.Character1} the [animal]\"), then use the lifespan tool to get the typical lifespan for that animal type.",
                    GetAnimalLifespan(fact.Animal1), new List<string> { "get_animal_lifespan" }),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} met {fact.Character2}. Find what type of animal {fact.Character2} is from the chapter (look for \"{fact.Character2} the [animal]\"), then use the lifespan tool to get the typical lifespan for that animal type.",
                    GetAnimalLifespan(fact.Animal2), new List<string> { "get_animal_lifespan" }),

                // get_animal_weight questions - model must find the animal type from context
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character2} received gifts from {fact.Character1}. Find what type of animal {fact.Character2} is from the chapter, then use the weight tool to get the average weight for that animal type.",
                    GetAnimalWeight(fact.Animal2), new List<string> { "get_animal_weight" }),
                () => ($"Quiz: In Chapter {fact.Chapter}, {fact.Character1} gave gifts to {fact.Character2}. Find what type of animal {fact.Character1} is from the chapter, then use the weight tool to get the average weight for that animal type.",
                    GetAnimalWeight(fact.Animal1), new List<string> { "get_animal_weight" })
            };

            var (text, answer, tools) = toolQuestionTypes[_random.Next(toolQuestionTypes.Count)]();

            return new BenchQuestion
            {
                Id = id,
                Text = text,
                ReferenceAnswer = answer,
                AboutCategory = aboutCategory,
                ExpectedTools = tools
            };
        }

        #region Tool Data Helpers

        private string GetWeatherTemp(string city)
        {
            // Generate deterministic temperature based on city name hash
            // Range: 8C to 38C (30 degree range)
            var hash = Math.Abs(city.ToLowerInvariant().GetHashCode());
            var temp = 8 + (hash % 31);
            return $"{temp}C";
        }

        private string GetDistance(string from, string to)
        {
            // Generate deterministic distance based on combined city names
            // Ensures same distance regardless of direction
            var combined = string.Join("|", new[] { from.ToLowerInvariant(), to.ToLowerInvariant() }.OrderBy(x => x));
            var hash = Math.Abs(combined.GetHashCode());
            // Range: 50 to 500 km
            var dist = 50 + (hash % 451);
            return $"{dist} km";
        }

        private string GetTravelTime(string from, string to, string mode)
        {
            var distStr = GetDistance(from, to);
            var dist = int.Parse(distStr.Replace(" km", ""));
            var speed = mode == "horse" ? 40 : mode == "walk" ? 5 : 25;
            var hours = (double)dist / speed;
            return $"{hours:F1} hours";
        }

        private string GetPopulation(string city)
        {
            // Generate deterministic population based on city name hash
            // Range: 5,000 to 100,000
            var hash = Math.Abs(city.ToLowerInvariant().GetHashCode());
            var pop = 5000 + (hash % 95001);
            return pop.ToString("N0");
        }

        private string GetAnimalLifespan(string animal)
        {
            var spans = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Original animals
                { "Tiger", 15 }, { "Turtle", 80 }, { "Gorilla", 40 }, { "Eagle", 25 },
                { "Dolphin", 45 }, { "Elephant", 70 }, { "Fox", 10 }, { "Bear", 30 },
                { "Wolf", 13 }, { "Owl", 20 }, { "Lion", 14 }, { "Leopard", 15 },
                // Additional animals from Animals array
                { "Rabbit", 9 }, { "Deer", 20 }, { "Cheetah", 12 }, { "Panther", 15 },
                { "Horse", 28 }, { "Donkey", 30 }, { "Zebra", 25 }, { "Giraffe", 25 },
                { "Hippo", 45 }, { "Rhino", 40 }, { "Crocodile", 70 }, { "Parrot", 50 },
                { "Penguin", 20 }, { "Flamingo", 40 }, { "Pelican", 25 }, { "Swan", 20 },
                { "Beaver", 12 }, { "Otter", 15 }, { "Seal", 30 }, { "Whale", 70 },
                { "Shark", 25 }, { "Octopus", 3 }, { "Crab", 8 }, { "Lobster", 50 },
                { "Peacock", 20 }, { "Crane", 30 }, { "Heron", 15 }, { "Falcon", 18 }
            };
            return $"{spans.GetValueOrDefault(animal, 20)} years";
        }

        private string GetAnimalWeight(string animal)
        {
            var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Original animals
                { "Tiger", 220 }, { "Turtle", 300 }, { "Gorilla", 180 }, { "Eagle", 6 },
                { "Dolphin", 200 }, { "Elephant", 5000 }, { "Fox", 8 }, { "Bear", 400 },
                { "Wolf", 45 }, { "Owl", 2 }, { "Lion", 190 }, { "Leopard", 70 },
                // Additional animals from Animals array
                { "Rabbit", 2 }, { "Deer", 100 }, { "Cheetah", 55 }, { "Panther", 65 },
                { "Horse", 500 }, { "Donkey", 250 }, { "Zebra", 350 }, { "Giraffe", 1200 },
                { "Hippo", 1800 }, { "Rhino", 2000 }, { "Crocodile", 450 }, { "Parrot", 1 },
                { "Penguin", 35 }, { "Flamingo", 3 }, { "Pelican", 10 }, { "Swan", 12 },
                { "Beaver", 25 }, { "Otter", 12 }, { "Seal", 250 }, { "Whale", 40000 },
                { "Shark", 700 }, { "Octopus", 15 }, { "Crab", 1 }, { "Lobster", 5 },
                { "Peacock", 5 }, { "Crane", 6 }, { "Heron", 2 }, { "Falcon", 1 }
            };
            return $"{weights.GetValueOrDefault(animal, 50)} kg";
        }

        #endregion

        #region File Generation

        /// <summary>
        /// Validation result for a category showing tokenized content vs available context.
        /// </summary>
        public class CategoryValidation
        {
            public string Category { get; set; } = "";
            public int TargetTokens { get; set; }

            /// <summary>
            /// Actual max tokens for testing with non-thinking overhead
            /// </summary>
            public int MaxTokensNonThinking { get; set; }

            /// <summary>
            /// Actual max tokens for testing with thinking overhead
            /// </summary>
            public int MaxTokensThinking { get; set; }

            // Tokenized content (accurately measured)
            public int ContextTokens { get; set; }           // Accumulated story context up to this category
            public int QuestionTokens { get; set; }          // Question text tokens (accumulated)
            public int InstructionsTokens { get; set; }      // Instructions tokens (once)

            /// <summary>
            /// Total tokenized content = context + questions + instructions
            /// This is what we can accurately measure.
            /// </summary>
            public int TotalTokenized { get; set; }

            public double PercentOfTarget => TargetTokens > 0 ? TotalTokenized * 100.0 / TargetTokens : 0;
            public double PercentNonThinking => MaxTokensNonThinking > 0 ? TotalTokenized * 100.0 / MaxTokensNonThinking : 0;
            public double PercentThinking => MaxTokensThinking > 0 ? TotalTokenized * 100.0 / MaxTokensThinking : 0;

            public bool IsOverflowNonThinking => TotalTokenized > MaxTokensNonThinking;
            public bool IsOverflowThinking => TotalTokenized > MaxTokensThinking;
        }

        /// <summary>
        /// Validate a test suite's context usage by tokenizing all generated content.
        /// </summary>
        public static List<CategoryValidation> ValidateContextUsage(BenchTestSuite suite)
        {
            var results = new List<CategoryValidation>();
            int accumulatedContextTokens = 0;
            int accumulatedQuestionTokens = 0;

            // Get overhead values from suite
            int overheadNonThinking = suite.ContextLengthOverhead > 0 ? suite.ContextLengthOverhead : 2048;
            int overheadThinking = suite.ContextLengthOverheadThinking > 0 ? suite.ContextLengthOverheadThinking : 4096;

            // Instructions tokens (sent once at start) - tokenized
            int instructionsTokens = suite.Instructions != null
                ? BenchTokenizer.EstimateTokenCount(suite.Instructions)
                : 0;

            for (int i = 0; i < suite.Categories.Count; i++)
            {
                var category = suite.Categories[i];

                // Tokenize this category's context
                int categoryContextTokens = BenchTokenizer.EstimateTokenCount(category.Context);
                accumulatedContextTokens += categoryContextTokens;

                // Tokenize questions for this category (accumulated across all categories)
                int categoryQuestionTokens = 0;
                if (category.Questions != null)
                {
                    foreach (var q in category.Questions)
                    {
                        categoryQuestionTokens += BenchTokenizer.EstimateTokenCount(q.Text);
                    }
                }
                if (category.SubCategories != null)
                {
                    foreach (var sub in category.SubCategories)
                    {
                        foreach (var q in sub.Questions)
                        {
                            categoryQuestionTokens += BenchTokenizer.EstimateTokenCount(q.Text);
                        }
                    }
                }
                accumulatedQuestionTokens += categoryQuestionTokens;

                var validation = new CategoryValidation
                {
                    Category = category.Name,
                    TargetTokens = category.ContextLength,
                    MaxTokensNonThinking = category.ContextLength + overheadNonThinking,
                    MaxTokensThinking = category.ContextLength + overheadThinking,
                    ContextTokens = accumulatedContextTokens,
                    QuestionTokens = accumulatedQuestionTokens,
                    InstructionsTokens = instructionsTokens,
                    TotalTokenized = accumulatedContextTokens + accumulatedQuestionTokens + instructionsTokens
                };

                results.Add(validation);
            }

            return results;
        }

        /// <summary>
        /// Print context validation results to console
        /// </summary>
        public static void PrintContextValidation(BenchTestSuite suite)
        {
            var validation = ValidateContextUsage(suite);

            // Get overhead values
            int overheadNonThinking = suite.ContextLengthOverhead > 0 ? suite.ContextLengthOverhead : 2048;
            int overheadThinking = suite.ContextLengthOverheadThinking > 0 ? suite.ContextLengthOverheadThinking : 4096;

            Console.WriteLine($"\n=== Context Validation for {suite.TestType} ===");
            Console.WriteLine($"Overhead: {overheadNonThinking:N0} (non-thinking) / {overheadThinking:N0} (thinking)");
            Console.WriteLine($"{"Cat",-5} {"Target",-7} {"Content",-8} {"MaxNoTh",-8} {"%",-6} {"MaxTh",-8} {"%",-6} {"Status",-10}");
            Console.WriteLine(new string('-', 72));

            foreach (var v in validation)
            {
                // Warning threshold: 95% for large contexts (>128k), 90% for smaller
                var warningThreshold = v.TargetTokens > 131072 ? 95.0 : 90.0;

                string status;
                if (v.IsOverflowThinking)
                    status = "OVERFLOW!";
                else if (v.IsOverflowNonThinking)
                    status = "OK(think)";
                else if (v.PercentNonThinking > warningThreshold)
                    status = "WARNING";
                else
                    status = "OK";

                Console.WriteLine($"{v.Category,-5} {v.TargetTokens,-7:N0} {v.TotalTokenized,-8:N0} {v.MaxTokensNonThinking,-8:N0} {v.PercentNonThinking,-6:F1} {v.MaxTokensThinking,-8:N0} {v.PercentThinking,-6:F1} {status,-10}");
            }

            var overflowThinkingCount = validation.Count(v => v.IsOverflowThinking);
            var overflowNonThinkingCount = validation.Count(v => v.IsOverflowNonThinking);
            Console.WriteLine();
            Console.WriteLine("Legend: Content = tokenized (context+questions+instructions), MaxNoTh/MaxTh = target+overhead");
            Console.WriteLine("        OK(think) = fits with thinking overhead only");
            if (overflowThinkingCount > 0)
            {
                Console.WriteLine($"[WARNING] {overflowThinkingCount} category(ies) overflow even with thinking overhead!");
            }
            else if (overflowNonThinkingCount > 0)
            {
                Console.WriteLine($"[INFO] {overflowNonThinkingCount} category(ies) require thinking overhead (--nothinking may overflow)");
            }
            else
            {
                Console.WriteLine($"[OK] All categories within context limits");
            }
        }

        /// <summary>
        /// Generate and save a ctxbench test suite to a JSON file
        /// </summary>
        public void SaveCtxBenchSuite(string filePath)
        {
            var suite = GenerateCtxBenchSuite();
            SaveSuiteToFile(suite, filePath);
        }

        /// <summary>
        /// Generate and save a ctxtoolsbench test suite to a JSON file
        /// </summary>
        public void SaveCtxToolsBenchSuite(string filePath)
        {
            var suite = GenerateCtxToolsBenchSuite();
            SaveSuiteToFile(suite, filePath);
        }

        private void SaveSuiteToFile(BenchTestSuite suite, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(suite, options);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Generate both default test suites to a directory
        /// </summary>
        /// <param name="directory">Output directory</param>
        /// <param name="contentScalePercent">Content scaling percentage (50-150). 100 = calibrated default. Null = 100%.</param>
        public static void GenerateDefaultSuites(string directory, int? contentScalePercent = null)
        {
            Directory.CreateDirectory(directory);

            var generator = new BenchStoryGenerator(42, contentScalePercent); // Fixed seed for reproducibility
            generator.SaveCtxBenchSuite(Path.Combine(directory, "v1ctxbench.json"));

            // Reset for second suite
            generator = new BenchStoryGenerator(42, contentScalePercent);
            generator.SaveCtxToolsBenchSuite(Path.Combine(directory, "v1ctxtoolsbench.json"));
        }

        #endregion

        #region Helper Methods

        // Maps character names to their animal identity for consistency across all chapters
        private readonly Dictionary<string, string> _characterAnimals = new();
        // Tracks which characters have been used in the current chapter (to avoid duplicates within same chapter)
        private readonly HashSet<string> _usedInCurrentChapter = new();

        // Cross-chapter tracking for atomic information (no repeats across chapters)
        private readonly HashSet<string> _usedCharactersGlobal = new();
        private readonly HashSet<string> _usedLocationsGlobal = new();
        private readonly HashSet<string> _usedItemsGlobal = new();
        private readonly HashSet<int> _usedQuantitiesGlobal = new();

        private (string name, string animal) GetUniqueCharacter()
        {
            string name;
            int attempts = 0;

            // Find a character name not yet used GLOBALLY (across all chapters)
            do
            {
                name = GetRandomElement(Names);
                attempts++;
            } while (_usedCharactersGlobal.Contains(name) && attempts < 100);

            _usedCharactersGlobal.Add(name);
            _usedInCurrentChapter.Add(name);

            // Assign a unique animal for this character
            string animal;
            attempts = 0;
            do
            {
                animal = GetRandomElement(Animals);
                attempts++;
            } while (_characterAnimals.Values.Contains(animal) && attempts < 100);

            _characterAnimals[name] = animal;

            return (name, animal);
        }

        private string GetUniqueLocation()
        {
            string location;
            int attempts = 0;
            do
            {
                location = GetRandomElement(Locations);
                attempts++;
            } while (_usedLocationsGlobal.Contains(location) && attempts < 100);

            _usedLocationsGlobal.Add(location);
            return location;
        }

        private string GetUniqueItem()
        {
            string item;
            int attempts = 0;
            do
            {
                item = GetRandomElement(Items);
                attempts++;
            } while (_usedItemsGlobal.Contains(item) && attempts < 100);

            _usedItemsGlobal.Add(item);
            return item;
        }

        private int GetUniqueQuantity(int min = 5, int max = 100)
        {
            int quantity;
            int attempts = 0;
            do
            {
                quantity = _random.Next(min, max);
                attempts++;
            } while (_usedQuantitiesGlobal.Contains(quantity) && attempts < 100);

            _usedQuantitiesGlobal.Add(quantity);
            return quantity;
        }

        private void ResetChapterCharacterTracking()
        {
            // Reset per-chapter tracking (called at start of each chapter)
            _usedInCurrentChapter.Clear();
        }

        private void ResetGlobalTracking()
        {
            // Reset all global tracking (called at start of test suite generation)
            _usedCharactersGlobal.Clear();
            _usedLocationsGlobal.Clear();
            _usedItemsGlobal.Clear();
            _usedQuantitiesGlobal.Clear();
            _characterAnimals.Clear();
        }

        private string GetRandomElement(string[] array) => array[_random.Next(array.Length)];

        private string GetDifferentElement(string[] array, string exclude)
        {
            string result;
            int attempts = 0;
            do
            {
                result = array[_random.Next(array.Length)];
                attempts++;
            } while (result == exclude && attempts < 50);
            return result;
        }

        private string GetDefaultInstructions()
        {
            return @"You will be presented with chapters from a book called ""CHRONICLES OF THE ANIMAL TRIBE"". Read each chapter carefully and memorize ALL details including: character names, their animal types, locations, quantities, items, and journey durations.

After reading, you will be asked quiz questions about the content. Answer each question accurately and concisely based ONLY on what you read in the chapters.";
        }

        private string GetToolsInstructions()
        {
            return @"You will be presented with chapters from a book called ""CHRONICLES OF THE ANIMAL TRIBE"". Read each chapter carefully and memorize ALL details including: character names, their animal types, locations, quantities, items, and journey durations.

IMPORTANT: After reading, you will be asked quiz questions that require you to:
1. FIRST retrieve specific information from the chapters (names, numbers, cities, animal types)
2. THEN use the provided tools to calculate or look up additional information

The chapters contain all the information you need to query the tools correctly. For example:
- To find an animal's lifespan, first identify the animal TYPE from the chapter (e.g., ""Maple the Hippo"" means Maple is a Hippo), then use get_animal_lifespan with ""hippo""
- To find weather, first identify which CITY the character traveled to, then use get_weather with that city name
- To use the magic calculator, first find the QUANTITY mentioned in the chapter, then perform the requested operation

Answer each question by using the tools with the correct information from the chapters.";
        }

        private string GetDefaultJudgePrompt()
        {
            return @"You are an impartial judge evaluating answer correctness.

TASK: Check if the model answer contains ""%%REF_ANSWER%%"" (or equivalent) for the question ""%%QUESTION%%""

RULES:
1. ONLY check if the reference answer appears in the model's response
2. Accept SYNONYMS (gifted=gave=delivered, traveled from=came from=departed from)
3. Accept EXTRA DETAILS - additional information is OK
4. DO NOT consider any other questions or topics - focus ONLY on this specific question

Question: %%QUESTION%%
Reference Answer: %%REF_ANSWER%%
Model Answer: %%MODEL_ANSWER%%

Does the model answer contain ""%%REF_ANSWER%%"" or equivalent? Respond with JSON only:
{""Answer"": ""YES"" or ""NO"", ""Reason"": ""brief explanation""}";
        }

        private string GetDefaultJudgeSystemPrompt()
        {
            return @"You are an impartial judge evaluating answer correctness.";
        }

        #endregion

        private class FactInfo
        {
            public int Chapter { get; set; }
            public string Character1 { get; set; } = "";
            public string Animal1 { get; set; } = "";
            public string Character2 { get; set; } = "";
            public string Animal2 { get; set; } = "";
            public string Action { get; set; } = "";
            public int Quantity { get; set; }
            public string Item { get; set; } = "";
            public string FromLocation { get; set; } = "";
            public string ToLocation { get; set; } = "";
            public string Terrain { get; set; } = "";
            public int Duration { get; set; }
        }
    }
}
