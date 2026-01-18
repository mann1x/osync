using System.Text.Json;
using System.Text.Json.Nodes;

namespace osync
{
    /// <summary>
    /// Benchmark tools for ctxtoolsbench test type.
    /// Provides 15 tools: 1 rule-based calculator + 14 static data tools.
    /// </summary>
    public static class BenchTools
    {
        #region Tool Definitions for Ollama API

        /// <summary>
        /// Get all tool definitions for Ollama chat API
        /// </summary>
        public static List<object> GetAllToolDefinitions()
        {
            return new List<object>
            {
                GetMagicCalculatorDefinition(),
                GetNumberOfPiesDefinition(),
                GetWeatherDefinition(),
                GetDistanceDefinition(),
                GetTravelTimeDefinition(),
                GetPopulationDefinition(),
                GetAnimalLifespanDefinition(),
                GetAnimalWeightDefinition(),
                GetFruitSeasonDefinition(),
                GetRecipeIngredientsDefinition(),
                GetBookPagesDefinition(),
                GetMountainHeightDefinition(),
                GetRiverLengthDefinition(),
                GetCurrencyExchangeDefinition(),
                GetPlanetDistanceDefinition()
            };
        }

        /// <summary>
        /// Get tool definitions by name list (null = all tools)
        /// </summary>
        public static List<object> GetToolDefinitions(List<string>? toolNames)
        {
            if (toolNames == null || toolNames.Count == 0)
                return GetAllToolDefinitions();

            var all = GetAllToolDefinitions();
            var result = new List<object>();

            foreach (var tool in all)
            {
                var json = JsonSerializer.Serialize(tool);
                var node = JsonNode.Parse(json);
                var name = node?["function"]?["name"]?.GetValue<string>();
                if (name != null && toolNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(tool);
                }
            }

            return result;
        }

        private static object GetMagicCalculatorDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "magic_calculator",
                description = "A magic calculator for the magic domain. Performs basic arithmetic with magical adjustments: additions and multiplications return (result)+1, subtractions and divisions return (result)-1.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        operation = new
                        {
                            type = "string",
                            description = "The operation to perform: add, subtract, multiply, or divide",
                            @enum = new[] { "add", "subtract", "multiply", "divide" }
                        },
                        a = new
                        {
                            type = "number",
                            description = "The first number"
                        },
                        b = new
                        {
                            type = "number",
                            description = "The second number"
                        }
                    },
                    required = new[] { "operation", "a", "b" }
                }
            }
        };

        private static object GetNumberOfPiesDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_number_of_pies",
                description = "Get the number of pies you can make with a given amount of fruit. Different fruits require different amounts per pie.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        fruit = new
                        {
                            type = "string",
                            description = "The type of fruit (e.g., apple, banana, strawberry, cherry, blueberry, peach, pear, mango, orange, grape)"
                        },
                        amount = new
                        {
                            type = "integer",
                            description = "The amount of fruit available"
                        }
                    },
                    required = new[] { "fruit", "amount" }
                }
            }
        };

        private static object GetWeatherDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_weather",
                description = "Get the current weather conditions for a city including temperature, conditions, and humidity.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        city = new
                        {
                            type = "string",
                            description = "The city name (e.g., Forest City, Mountain Village, River Town, Desert Oasis, Crystal Lake)"
                        }
                    },
                    required = new[] { "city" }
                }
            }
        };

        private static object GetDistanceDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_distance",
                description = "Get the distance in kilometers between two cities.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        from_city = new
                        {
                            type = "string",
                            description = "The origin city"
                        },
                        to_city = new
                        {
                            type = "string",
                            description = "The destination city"
                        }
                    },
                    required = new[] { "from_city", "to_city" }
                }
            }
        };

        private static object GetTravelTimeDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_travel_time",
                description = "Get the travel time in hours between two cities by a specified mode of transport.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        from_city = new
                        {
                            type = "string",
                            description = "The origin city"
                        },
                        to_city = new
                        {
                            type = "string",
                            description = "The destination city"
                        },
                        mode = new
                        {
                            type = "string",
                            description = "Mode of transport: walk, horse, carriage, or flying",
                            @enum = new[] { "walk", "horse", "carriage", "flying" }
                        }
                    },
                    required = new[] { "from_city", "to_city", "mode" }
                }
            }
        };

        private static object GetPopulationDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_population",
                description = "Get the population of a city.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        city = new
                        {
                            type = "string",
                            description = "The city name"
                        }
                    },
                    required = new[] { "city" }
                }
            }
        };

        private static object GetAnimalLifespanDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_animal_lifespan",
                description = "Get the average lifespan in years of an animal species.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        animal = new
                        {
                            type = "string",
                            description = "The animal species (e.g., tiger, turtle, gorilla, eagle, dolphin)"
                        }
                    },
                    required = new[] { "animal" }
                }
            }
        };

        private static object GetAnimalWeightDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_animal_weight",
                description = "Get the average weight in kilograms of an animal species.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        animal = new
                        {
                            type = "string",
                            description = "The animal species"
                        }
                    },
                    required = new[] { "animal" }
                }
            }
        };

        private static object GetFruitSeasonDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_fruit_season",
                description = "Get the harvest season for a type of fruit.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        fruit = new
                        {
                            type = "string",
                            description = "The type of fruit"
                        }
                    },
                    required = new[] { "fruit" }
                }
            }
        };

        private static object GetRecipeIngredientsDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_recipe_ingredients",
                description = "Get the number of ingredients needed for a recipe.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        recipe = new
                        {
                            type = "string",
                            description = "The recipe name (e.g., apple pie, banana bread, fruit salad, vegetable soup)"
                        }
                    },
                    required = new[] { "recipe" }
                }
            }
        };

        private static object GetBookPagesDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_book_pages",
                description = "Get the number of pages in a book.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        book = new
                        {
                            type = "string",
                            description = "The book title"
                        }
                    },
                    required = new[] { "book" }
                }
            }
        };

        private static object GetMountainHeightDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_mountain_height",
                description = "Get the height in meters of a mountain.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        mountain = new
                        {
                            type = "string",
                            description = "The mountain name"
                        }
                    },
                    required = new[] { "mountain" }
                }
            }
        };

        private static object GetRiverLengthDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_river_length",
                description = "Get the length in kilometers of a river.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        river = new
                        {
                            type = "string",
                            description = "The river name"
                        }
                    },
                    required = new[] { "river" }
                }
            }
        };

        private static object GetCurrencyExchangeDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_currency_exchange",
                description = "Get the exchange rate of a currency to Gold Coins (the standard currency).",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        currency = new
                        {
                            type = "string",
                            description = "The currency name (e.g., Silver Coins, Copper Coins, Gems, Pearls)"
                        }
                    },
                    required = new[] { "currency" }
                }
            }
        };

        private static object GetPlanetDistanceDefinition() => new
        {
            type = "function",
            function = new
            {
                name = "get_planet_distance",
                description = "Get the distance from the home world to another celestial body in million kilometers.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        planet = new
                        {
                            type = "string",
                            description = "The celestial body name"
                        }
                    },
                    required = new[] { "planet" }
                }
            }
        };

        #endregion

        #region Tool Execution

        /// <summary>
        /// Execute a tool call and return the result
        /// </summary>
        public static string ExecuteTool(string toolName, JsonNode? arguments)
        {
            return toolName.ToLowerInvariant() switch
            {
                "magic_calculator" => ExecuteMagicCalculator(arguments),
                "get_number_of_pies" => ExecuteGetNumberOfPies(arguments),
                "get_weather" => ExecuteGetWeather(arguments),
                "get_distance" => ExecuteGetDistance(arguments),
                "get_travel_time" => ExecuteGetTravelTime(arguments),
                "get_population" => ExecuteGetPopulation(arguments),
                "get_animal_lifespan" => ExecuteGetAnimalLifespan(arguments),
                "get_animal_weight" => ExecuteGetAnimalWeight(arguments),
                "get_fruit_season" => ExecuteGetFruitSeason(arguments),
                "get_recipe_ingredients" => ExecuteGetRecipeIngredients(arguments),
                "get_book_pages" => ExecuteGetBookPages(arguments),
                "get_mountain_height" => ExecuteGetMountainHeight(arguments),
                "get_river_length" => ExecuteGetRiverLength(arguments),
                "get_currency_exchange" => ExecuteGetCurrencyExchange(arguments),
                "get_planet_distance" => ExecuteGetPlanetDistance(arguments),
                _ => $"Unknown tool: {toolName}"
            };
        }

        private static string ExecuteMagicCalculator(JsonNode? args)
        {
            var operation = args?["operation"]?.GetValue<string>()?.ToLowerInvariant();
            var a = args?["a"]?.GetValue<double>() ?? 0;
            var b = args?["b"]?.GetValue<double>() ?? 0;

            double result = operation switch
            {
                "add" => (a + b) + 1,
                "subtract" => (a - b) - 1,
                "multiply" => (a * b) + 1,
                "divide" when b != 0 => (a / b) - 1,
                "divide" => double.NaN,
                _ => double.NaN
            };

            if (double.IsNaN(result))
                return "Error: Invalid operation or division by zero";

            // Return integer if result is whole number
            if (result == Math.Floor(result))
                return ((int)result).ToString();
            return result.ToString("F2");
        }

        private static string ExecuteGetNumberOfPies(JsonNode? args)
        {
            var fruit = args?["fruit"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
            var amount = args?["amount"]?.GetValue<int>() ?? 0;

            // Fruit amounts needed per pie
            var amountsPerPie = new Dictionary<string, int>
            {
                { "apple", 10 }, { "banana", 5 }, { "strawberry", 100 }, { "cherry", 50 },
                { "blueberry", 200 }, { "peach", 8 }, { "pear", 10 }, { "mango", 6 },
                { "orange", 12 }, { "grape", 150 }, { "raspberry", 120 }, { "blackberry", 100 },
                { "plum", 15 }, { "apricot", 12 }, { "fig", 20 }, { "kiwi", 10 },
                { "watermelon", 1 }, { "cantaloupe", 2 }, { "honeydew", 2 }, { "papaya", 3 },
                { "pineapple", 2 }, { "coconut", 4 }, { "lemon", 15 }, { "lime", 20 },
                { "grapefruit", 6 }, { "tangerine", 15 }, { "pomegranate", 5 }, { "persimmon", 8 },
                { "guava", 10 }, { "passion fruit", 25 }, { "dragon fruit", 4 }, { "lychee", 30 },
                { "rambutan", 35 }, { "durian", 1 }, { "jackfruit", 1 }, { "starfruit", 8 },
                { "mulberry", 150 }, { "gooseberry", 80 }, { "cranberry", 200 }, { "elderberry", 180 },
                { "acai", 100 }, { "goji berry", 150 }, { "boysenberry", 90 }, { "lingonberry", 120 },
                { "currant", 200 }, { "date", 30 }, { "prune", 25 }, { "raisin", 200 },
                { "nectarine", 8 }, { "quince", 6 }
            };

            if (!amountsPerPie.TryGetValue(fruit, out var perPie))
                return $"Unknown fruit: {fruit}";

            var pies = amount / perPie;
            return pies.ToString();
        }

        private static string ExecuteGetWeather(JsonNode? args)
        {
            var city = args?["city"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(city))
                return "City name is required";

            // Generate deterministic temperature based on city name hash
            // Range: 8C to 38C (30 degree range)
            var hash = Math.Abs(city.GetHashCode());
            var temp = 8 + (hash % 31);

            // Generate conditions based on temperature range
            string conditions;
            string humidity;
            if (temp < 12)
            {
                conditions = "Cold with light frost";
                humidity = $"{30 + (hash % 20)}%";
            }
            else if (temp < 20)
            {
                conditions = "Cool and pleasant";
                humidity = $"{45 + (hash % 25)}%";
            }
            else if (temp < 28)
            {
                conditions = "Warm with light breeze";
                humidity = $"{50 + (hash % 30)}%";
            }
            else
            {
                conditions = "Hot and sunny";
                humidity = $"{20 + (hash % 30)}%";
            }

            return $"Temperature: {temp}C, Conditions: {conditions}, Humidity: {humidity}";
        }

        private static string ExecuteGetDistance(JsonNode? args)
        {
            var from = args?["from_city"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
            var to = args?["to_city"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return "Both from_city and to_city are required";

            if (from == to)
                return "0 km";

            // Generate deterministic distance based on combined city names
            // Ensures same distance regardless of direction
            var combined = string.Join("|", new[] { from, to }.OrderBy(x => x));
            var hash = Math.Abs(combined.GetHashCode());
            // Range: 50 to 500 km
            var dist = 50 + (hash % 451);
            return $"{dist} km";
        }

        private static string ExecuteGetTravelTime(JsonNode? args)
        {
            var from = args?["from_city"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
            var to = args?["to_city"]?.GetValue<string>()?.ToLowerInvariant() ?? "";
            var mode = args?["mode"]?.GetValue<string>()?.ToLowerInvariant() ?? "walk";

            // Get distance first
            var distResult = ExecuteGetDistance(args);
            if (!distResult.EndsWith(" km"))
                return distResult; // Error message

            var distance = int.Parse(distResult.Replace(" km", ""));

            // Speed in km/h for each mode
            var speeds = new Dictionary<string, int>
            {
                { "walk", 5 },
                { "horse", 40 },
                { "carriage", 25 },
                { "flying", 100 }
            };

            if (!speeds.TryGetValue(mode, out var speed))
                return $"Unknown travel mode: {mode}";

            var hours = (double)distance / speed;
            if (hours < 1)
                return $"{(int)(hours * 60)} minutes";
            return $"{hours:F1} hours";
        }

        private static string ExecuteGetPopulation(JsonNode? args)
        {
            var city = args?["city"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(city))
                return "City name is required";

            // Generate deterministic population based on city name hash
            // Range: 5,000 to 100,000
            var hash = Math.Abs(city.GetHashCode());
            var pop = 5000 + (hash % 95001);
            return pop.ToString("N0");
        }

        private static string ExecuteGetAnimalLifespan(JsonNode? args)
        {
            var animal = args?["animal"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var lifespans = new Dictionary<string, int>
            {
                { "tiger", 15 }, { "turtle", 80 }, { "gorilla", 40 }, { "eagle", 25 },
                { "dolphin", 45 }, { "elephant", 70 }, { "fox", 10 }, { "bear", 30 },
                { "wolf", 13 }, { "owl", 20 }, { "rabbit", 9 }, { "deer", 20 },
                { "lion", 14 }, { "leopard", 15 }, { "cheetah", 12 }, { "panther", 15 },
                { "horse", 28 }, { "donkey", 30 }, { "zebra", 25 }, { "giraffe", 25 },
                { "hippo", 45 }, { "rhino", 40 }, { "crocodile", 70 }, { "alligator", 50 },
                { "snake", 20 }, { "lizard", 15 }, { "frog", 10 }, { "toad", 12 },
                { "parrot", 50 }, { "crow", 15 }, { "sparrow", 5 }, { "robin", 6 },
                { "penguin", 20 }, { "flamingo", 40 }, { "pelican", 25 }, { "swan", 20 },
                { "duck", 10 }, { "goose", 25 }, { "chicken", 8 }, { "turkey", 10 },
                { "beaver", 12 }, { "otter", 15 }, { "seal", 30 }, { "walrus", 35 },
                { "whale", 70 }, { "shark", 25 }, { "octopus", 3 }, { "squid", 2 },
                { "crab", 8 }, { "lobster", 50 },
                // Additional animals from Animals array
                { "peacock", 20 }, { "crane", 30 }, { "heron", 15 }, { "falcon", 18 }
            };

            if (!lifespans.TryGetValue(animal, out var years))
                return $"Lifespan data not available for: {animal}";

            return $"{years} years";
        }

        private static string ExecuteGetAnimalWeight(JsonNode? args)
        {
            var animal = args?["animal"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var weights = new Dictionary<string, int>
            {
                { "tiger", 220 }, { "turtle", 300 }, { "gorilla", 180 }, { "eagle", 6 },
                { "dolphin", 200 }, { "elephant", 5000 }, { "fox", 8 }, { "bear", 400 },
                { "wolf", 45 }, { "owl", 2 }, { "rabbit", 2 }, { "deer", 100 },
                { "lion", 190 }, { "leopard", 70 }, { "cheetah", 55 }, { "panther", 65 },
                { "horse", 500 }, { "donkey", 250 }, { "zebra", 350 }, { "giraffe", 1200 },
                { "hippo", 1800 }, { "rhino", 2000 }, { "crocodile", 450 }, { "alligator", 360 },
                { "snake", 10 }, { "lizard", 1 }, { "frog", 1 }, { "toad", 1 },
                { "parrot", 1 }, { "crow", 1 }, { "sparrow", 1 }, { "robin", 1 },
                { "penguin", 35 }, { "flamingo", 3 }, { "pelican", 10 }, { "swan", 12 },
                { "duck", 3 }, { "goose", 6 }, { "chicken", 3 }, { "turkey", 10 },
                { "beaver", 25 }, { "otter", 12 }, { "seal", 250 }, { "walrus", 1200 },
                { "whale", 40000 }, { "shark", 700 }, { "octopus", 15 }, { "squid", 20 },
                { "crab", 1 }, { "lobster", 5 },
                // Additional animals from Animals array
                { "peacock", 5 }, { "crane", 6 }, { "heron", 2 }, { "falcon", 1 }
            };

            if (!weights.TryGetValue(animal, out var kg))
                return $"Weight data not available for: {animal}";

            return $"{kg} kg";
        }

        private static string ExecuteGetFruitSeason(JsonNode? args)
        {
            var fruit = args?["fruit"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var seasons = new Dictionary<string, string>
            {
                { "apple", "Autumn" }, { "banana", "Year-round" }, { "strawberry", "Spring" },
                { "cherry", "Summer" }, { "blueberry", "Summer" }, { "peach", "Summer" },
                { "pear", "Autumn" }, { "mango", "Summer" }, { "orange", "Winter" },
                { "grape", "Autumn" }, { "raspberry", "Summer" }, { "blackberry", "Summer" },
                { "plum", "Summer" }, { "apricot", "Summer" }, { "fig", "Autumn" },
                { "kiwi", "Winter" }, { "watermelon", "Summer" }, { "cantaloupe", "Summer" },
                { "honeydew", "Summer" }, { "papaya", "Year-round" }, { "pineapple", "Spring" },
                { "coconut", "Year-round" }, { "lemon", "Winter" }, { "lime", "Year-round" },
                { "grapefruit", "Winter" }, { "tangerine", "Winter" }, { "pomegranate", "Autumn" },
                { "persimmon", "Autumn" }, { "guava", "Autumn" }, { "passion fruit", "Summer" },
                { "dragon fruit", "Summer" }, { "lychee", "Summer" }, { "rambutan", "Summer" },
                { "durian", "Summer" }, { "jackfruit", "Summer" }, { "starfruit", "Autumn" },
                { "mulberry", "Spring" }, { "gooseberry", "Summer" }, { "cranberry", "Autumn" },
                { "elderberry", "Autumn" }, { "acai", "Year-round" }, { "goji berry", "Summer" },
                { "boysenberry", "Summer" }, { "lingonberry", "Autumn" }, { "currant", "Summer" },
                { "date", "Autumn" }, { "prune", "Summer" }, { "raisin", "Autumn" },
                { "nectarine", "Summer" }, { "quince", "Autumn" }
            };

            if (!seasons.TryGetValue(fruit, out var season))
                return $"Season data not available for: {fruit}";

            return season;
        }

        private static string ExecuteGetRecipeIngredients(JsonNode? args)
        {
            var recipe = args?["recipe"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var ingredients = new Dictionary<string, int>
            {
                { "apple pie", 8 }, { "banana bread", 7 }, { "fruit salad", 6 },
                { "vegetable soup", 12 }, { "chocolate cake", 9 }, { "vanilla ice cream", 5 },
                { "strawberry jam", 3 }, { "orange juice", 2 }, { "lemonade", 4 },
                { "blueberry muffins", 8 }, { "cherry tart", 7 }, { "peach cobbler", 9 },
                { "pear crumble", 8 }, { "mango smoothie", 4 }, { "grape juice", 2 },
                { "raspberry sauce", 4 }, { "blackberry pie", 8 }, { "plum pudding", 11 },
                { "apricot preserve", 3 }, { "fig cookies", 7 }, { "kiwi sorbet", 4 },
                { "watermelon punch", 5 }, { "coconut curry", 14 }, { "lemon curd", 4 },
                { "lime pie", 7 }, { "grapefruit salad", 5 }, { "pomegranate sauce", 4 },
                { "honey cake", 8 }, { "carrot soup", 9 }, { "tomato sauce", 6 },
                { "mushroom risotto", 10 }, { "potato salad", 8 }, { "corn chowder", 11 },
                { "bean stew", 13 }, { "rice pudding", 6 }, { "oatmeal cookies", 7 },
                { "wheat bread", 5 }, { "rye crackers", 4 }, { "barley soup", 10 },
                { "quinoa bowl", 9 }, { "pasta primavera", 12 }, { "pizza dough", 5 },
                { "garlic butter", 3 }, { "herb seasoning", 8 }, { "spice blend", 10 },
                { "salad dressing", 6 }, { "meat marinade", 7 }, { "fish sauce", 5 },
                { "chicken broth", 8 }, { "beef stew", 15 }
            };

            if (!ingredients.TryGetValue(recipe, out var count))
                return $"Recipe not found: {recipe}";

            return $"{count} ingredients";
        }

        private static string ExecuteGetBookPages(JsonNode? args)
        {
            var book = args?["book"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var pages = new Dictionary<string, int>
            {
                { "the forest chronicles", 342 }, { "mountain legends", 256 }, { "river tales", 189 },
                { "desert wanderings", 412 }, { "crystal mysteries", 298 }, { "coastal adventures", 367 },
                { "meadow stories", 223 }, { "pine ridge mysteries", 445 }, { "sunset diaries", 178 },
                { "starlight poems", 156 }, { "bamboo wisdom", 234 }, { "cherry blossom dreams", 289 },
                { "golden sands", 378 }, { "silver cascade", 312 }, { "emerald secrets", 456 },
                { "sapphire depths", 389 }, { "ruby flames", 267 }, { "amber chronicles", 345 },
                { "ivory tower tales", 512 }, { "jade teachings", 198 }, { "obsidian shadows", 423 },
                { "coral kingdom", 356 }, { "thunder legends", 478 }, { "whisper memories", 234 },
                { "harvest songs", 167 }, { "moonlit tales", 289 }, { "sunfire saga", 534 },
                { "frostbite journey", 401 }, { "rainbow bridge", 245 }, { "twilight prophecy", 467 },
                { "blossom poetry", 134 }, { "autumn leaves", 212 }, { "winter solstice", 356 },
                { "summer dreams", 278 }, { "spring awakening", 189 }, { "copper tales", 234 },
                { "iron chronicles", 445 }, { "bronze age", 512 }, { "platinum quest", 378 },
                { "diamond heist", 298 }, { "opal visions", 267 }, { "topaz treasury", 345 },
                { "amethyst magic", 412 }, { "pearl divers", 223 }, { "turquoise waters", 189 },
                { "garnet heart", 256 }, { "onyx night", 389 }, { "citrine dawn", 312 },
                { "aquamarine voyage", 456 }, { "peridot garden", 234 }
            };

            if (!pages.TryGetValue(book, out var count))
                return $"Book not found: {book}";

            return $"{count} pages";
        }

        private static string ExecuteGetMountainHeight(JsonNode? args)
        {
            var mountain = args?["mountain"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var heights = new Dictionary<string, int>
            {
                { "crystal peak", 4500 }, { "thunder summit", 5200 }, { "obsidian mountain", 3800 },
                { "silver ridge", 4100 }, { "golden crest", 3600 }, { "emerald heights", 4800 },
                { "sapphire peak", 5500 }, { "ruby mountain", 3900 }, { "amber summit", 4200 },
                { "ivory spire", 6100 }, { "jade mountain", 3400 }, { "pearl peak", 2900 },
                { "diamond summit", 5800 }, { "opal heights", 4400 }, { "topaz ridge", 3700 },
                { "amethyst peak", 4600 }, { "turquoise mountain", 3200 }, { "garnet summit", 4900 },
                { "onyx spire", 5100 }, { "citrine peak", 3500 }, { "aquamarine ridge", 4000 },
                { "peridot mountain", 3300 }, { "copper crest", 2800 }, { "iron peak", 5400 },
                { "bronze summit", 3100 }, { "platinum heights", 6500 }, { "frost peak", 5900 },
                { "fire mountain", 4300 }, { "storm summit", 5000 }, { "cloud peak", 5600 },
                { "moon mountain", 4700 }, { "sun crest", 4150 }, { "star summit", 5300 },
                { "wind peak", 3850 }, { "rain mountain", 3650 }, { "snow ridge", 5750 },
                { "ice summit", 6200 }, { "mist peak", 4050 }, { "fog mountain", 3450 },
                { "dew heights", 2950 }, { "dawn peak", 4250 }, { "dusk summit", 4550 },
                { "twilight mountain", 4850 }, { "midnight peak", 5050 }, { "noon crest", 3950 },
                { "sunrise ridge", 4350 }, { "sunset summit", 4650 }, { "aurora peak", 5250 },
                { "eclipse mountain", 5450 }, { "solstice summit", 5650 }
            };

            if (!heights.TryGetValue(mountain, out var meters))
                return $"Mountain not found: {mountain}";

            return $"{meters} meters";
        }

        private static string ExecuteGetRiverLength(JsonNode? args)
        {
            var river = args?["river"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            var lengths = new Dictionary<string, int>
            {
                { "crystal river", 450 }, { "silver stream", 280 }, { "golden flow", 620 },
                { "emerald river", 380 }, { "sapphire stream", 520 }, { "ruby rapids", 190 },
                { "amber current", 340 }, { "jade river", 470 }, { "pearl stream", 230 },
                { "diamond flow", 680 }, { "opal rapids", 310 }, { "topaz river", 440 },
                { "amethyst stream", 290 }, { "turquoise flow", 560 }, { "garnet rapids", 210 },
                { "onyx river", 490 }, { "citrine stream", 350 }, { "aquamarine flow", 610 },
                { "peridot rapids", 260 }, { "copper river", 380 }, { "iron stream", 420 },
                { "bronze flow", 330 }, { "platinum rapids", 580 }, { "frost river", 510 },
                { "fire stream", 270 }, { "storm flow", 640 }, { "cloud rapids", 360 },
                { "moon river", 480 }, { "sun stream", 390 }, { "star flow", 550 },
                { "wind rapids", 240 }, { "rain river", 470 }, { "snow stream", 320 },
                { "ice flow", 590 }, { "mist rapids", 280 }, { "fog river", 410 },
                { "dew stream", 180 }, { "dawn flow", 530 }, { "dusk rapids", 350 },
                { "twilight river", 460 }, { "midnight stream", 380 }, { "noon flow", 290 },
                { "sunrise rapids", 420 }, { "sunset river", 510 }, { "aurora stream", 630 },
                { "eclipse flow", 370 }, { "solstice rapids", 440 }, { "equinox river", 560 },
                { "harvest stream", 310 }, { "spring flow", 480 }
            };

            if (!lengths.TryGetValue(river, out var km))
                return $"River not found: {river}";

            return $"{km} km";
        }

        private static string ExecuteGetCurrencyExchange(JsonNode? args)
        {
            var currency = args?["currency"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            // Exchange rates to Gold Coins
            var rates = new Dictionary<string, double>
            {
                { "silver coins", 0.1 }, { "copper coins", 0.01 }, { "bronze coins", 0.05 },
                { "platinum coins", 10.0 }, { "gems", 5.0 }, { "pearls", 2.5 },
                { "rubies", 8.0 }, { "sapphires", 7.5 }, { "emeralds", 6.5 },
                { "diamonds", 15.0 }, { "opals", 4.0 }, { "amethysts", 3.0 },
                { "topaz", 3.5 }, { "jade stones", 4.5 }, { "amber pieces", 2.0 },
                { "crystal shards", 1.5 }, { "obsidian chips", 0.5 }, { "iron ingots", 0.2 },
                { "steel bars", 0.8 }, { "mithril pieces", 25.0 }, { "adamantine shards", 50.0 },
                { "dragon scales", 100.0 }, { "phoenix feathers", 75.0 }, { "unicorn hair", 30.0 },
                { "sea shells", 0.02 }, { "river stones", 0.005 }, { "mountain crystals", 1.0 },
                { "forest tokens", 0.15 }, { "desert sand gold", 0.3 }, { "volcanic glass", 0.25 },
                { "moon silver", 12.0 }, { "sun gold", 20.0 }, { "star dust", 35.0 },
                { "rainbow essence", 45.0 }, { "thunder stones", 5.5 }, { "frost crystals", 3.2 },
                { "fire opals", 9.0 }, { "water pearls", 4.8 }, { "earth gems", 6.0 },
                { "wind chimes", 1.8 }, { "shadow coins", 0.08 }, { "light tokens", 0.12 },
                { "ancient coins", 2.2 }, { "rare stamps", 1.2 }, { "trading beads", 0.03 },
                { "merchant marks", 0.5 }, { "guild tokens", 0.75 }, { "royal seals", 5.0 },
                { "noble crests", 3.5 }, { "common currency", 0.1 }
            };

            if (!rates.TryGetValue(currency, out var rate))
                return $"Currency not found: {currency}";

            return $"1 {currency} = {rate} Gold Coins";
        }

        private static string ExecuteGetPlanetDistance(JsonNode? args)
        {
            var planet = args?["planet"]?.GetValue<string>()?.ToLowerInvariant() ?? "";

            // Distances in million kilometers from home world
            var distances = new Dictionary<string, double>
            {
                { "silver moon", 0.4 }, { "golden moon", 0.6 }, { "red planet", 78.0 },
                { "blue giant", 628.0 }, { "ring world", 1275.0 }, { "ice giant", 2870.0 },
                { "dark planet", 4500.0 }, { "crystal world", 150.0 }, { "fire planet", 42.0 },
                { "water world", 225.0 }, { "forest planet", 180.0 }, { "desert world", 95.0 },
                { "storm giant", 890.0 }, { "twin suns", 4200.0 }, { "nebula prime", 15000.0 },
                { "asteroid belt", 350.0 }, { "comet cluster", 5800.0 }, { "dwarf planet", 5900.0 },
                { "binary stars", 8500.0 }, { "pulsar point", 25000.0 }, { "black void", 50000.0 },
                { "white dwarf", 12000.0 }, { "neutron star", 35000.0 }, { "supernova remnant", 75000.0 },
                { "galactic core", 250000.0 }, { "outer rim", 100000.0 }, { "void station", 3200.0 },
                { "space port alpha", 1.2 }, { "orbital platform", 0.05 }, { "lunar base", 0.38 },
                { "mars colony", 78.3 }, { "jupiter station", 628.7 }, { "saturn rings", 1277.0 },
                { "titan base", 1280.0 }, { "europa lab", 630.0 }, { "ganymede outpost", 629.0 },
                { "io research", 627.0 }, { "callisto station", 631.0 }, { "triton base", 4350.0 },
                { "pluto frontier", 5906.0 }, { "eris outpost", 10125.0 }, { "sedna station", 13000.0 },
                { "oort cloud", 7500000.0 }, { "alpha centauri", 41300000.0 }, { "proxima b", 40200000.0 },
                { "barnards star", 59700000.0 }, { "sirius system", 81400000.0 }, { "vega prime", 237000000.0 },
                { "andromeda", 24000000000000.0 }
            };

            if (!distances.TryGetValue(planet, out var dist))
                return $"Celestial body not found: {planet}";

            if (dist >= 1000000)
                return $"{dist / 1000000:F1} trillion km";
            if (dist >= 1000)
                return $"{dist / 1000:F1} billion km";
            return $"{dist} million km";
        }

        #endregion

        #region ShowTools Output

        /// <summary>
        /// Generate formatted output for --showtools flag
        /// </summary>
        public static string GetToolsCheatSheet()
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=== BENCHMARK TOOLS REFERENCE ===");
            sb.AppendLine();
            sb.AppendLine("These tools are available for ctxtoolsbench test suites.");
            sb.AppendLine("All tools return deterministic results based on static data.");
            sb.AppendLine();

            // Magic Calculator
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: magic_calculator");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Magic calculator with adjusted results");
            sb.AppendLine("Parameters:");
            sb.AppendLine("  - operation: add, subtract, multiply, divide");
            sb.AppendLine("  - a: first number");
            sb.AppendLine("  - b: second number");
            sb.AppendLine("Rules:");
            sb.AppendLine("  - add(a, b)      = (a + b) + 1");
            sb.AppendLine("  - subtract(a, b) = (a - b) - 1");
            sb.AppendLine("  - multiply(a, b) = (a * b) + 1");
            sb.AppendLine("  - divide(a, b)   = (a / b) - 1");
            sb.AppendLine("Examples:");
            sb.AppendLine("  magic_calculator(add, 5, 3)      -> 9");
            sb.AppendLine("  magic_calculator(multiply, 4, 5) -> 21");
            sb.AppendLine();

            // Number of Pies
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_number_of_pies");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Calculate pies from fruit amount");
            sb.AppendLine("Parameters: fruit (string), amount (integer)");
            sb.AppendLine("Fruit requirements (per pie):");
            sb.AppendLine("  apple=10, banana=5, strawberry=100, cherry=50, blueberry=200");
            sb.AppendLine("  peach=8, pear=10, mango=6, orange=12, grape=150");
            sb.AppendLine("  raspberry=120, blackberry=100, watermelon=1, pineapple=2");
            sb.AppendLine();

            // Weather
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_weather");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get weather for a city");
            sb.AppendLine("Parameters: city (string)");
            sb.AppendLine("Available cities: Forest City (22C), Mountain Village (15C),");
            sb.AppendLine("  River Town (24C), Desert Oasis (35C), Crystal Lake (18C),");
            sb.AppendLine("  Coastal Haven (26C), Meadow Valley (20C), Pine Ridge (12C),");
            sb.AppendLine("  + 42 more cities in the database");
            sb.AppendLine();

            // Distance
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_distance");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get distance between two cities in km");
            sb.AppendLine("Parameters: from_city (string), to_city (string)");
            sb.AppendLine("Sample distances:");
            sb.AppendLine("  Forest City -> Mountain Village: 150 km");
            sb.AppendLine("  Forest City -> River Town: 80 km");
            sb.AppendLine("  Desert Oasis -> Crystal Lake: 380 km");
            sb.AppendLine();

            // Travel Time
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_travel_time");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get travel time between cities");
            sb.AppendLine("Parameters: from_city, to_city, mode (walk/horse/carriage/flying)");
            sb.AppendLine("Speed by mode: walk=5km/h, horse=40km/h, carriage=25km/h, flying=100km/h");
            sb.AppendLine();

            // Population
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_population");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get city population");
            sb.AppendLine("Parameters: city (string)");
            sb.AppendLine("Sample: Forest City=45000, Coastal Haven=55000, Citrine City=48000");
            sb.AppendLine();

            // Animal Lifespan
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_animal_lifespan");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get animal lifespan in years");
            sb.AppendLine("Parameters: animal (string)");
            sb.AppendLine("Sample: tiger=15, turtle=80, gorilla=40, eagle=25, elephant=70");
            sb.AppendLine();

            // Animal Weight
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_animal_weight");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get animal weight in kg");
            sb.AppendLine("Parameters: animal (string)");
            sb.AppendLine("Sample: tiger=220, turtle=300, gorilla=180, elephant=5000");
            sb.AppendLine();

            // Fruit Season
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_fruit_season");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get harvest season for fruit");
            sb.AppendLine("Parameters: fruit (string)");
            sb.AppendLine("Sample: apple=Autumn, strawberry=Spring, cherry=Summer, orange=Winter");
            sb.AppendLine();

            // Recipe Ingredients
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_recipe_ingredients");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get number of ingredients for a recipe");
            sb.AppendLine("Parameters: recipe (string)");
            sb.AppendLine("Sample: apple pie=8, banana bread=7, vegetable soup=12, beef stew=15");
            sb.AppendLine();

            // Book Pages
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_book_pages");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get page count of a book");
            sb.AppendLine("Parameters: book (string)");
            sb.AppendLine("Sample: The Forest Chronicles=342, Ivory Tower Tales=512");
            sb.AppendLine();

            // Mountain Height
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_mountain_height");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get mountain height in meters");
            sb.AppendLine("Parameters: mountain (string)");
            sb.AppendLine("Sample: Crystal Peak=4500, Platinum Heights=6500, Ice Summit=6200");
            sb.AppendLine();

            // River Length
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_river_length");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get river length in km");
            sb.AppendLine("Parameters: river (string)");
            sb.AppendLine("Sample: Crystal River=450, Diamond Flow=680, Storm Flow=640");
            sb.AppendLine();

            // Currency Exchange
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_currency_exchange");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get exchange rate to Gold Coins");
            sb.AppendLine("Parameters: currency (string)");
            sb.AppendLine("Sample: Silver Coins=0.1, Platinum Coins=10, Diamonds=15, Gems=5");
            sb.AppendLine();

            // Planet Distance
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOOL: get_planet_distance");
            sb.AppendLine("─────────────────────────────────────────────────────────────────");
            sb.AppendLine("Description: Get distance to celestial body in million km");
            sb.AppendLine("Parameters: planet (string)");
            sb.AppendLine("Sample: Silver Moon=0.4, Red Planet=78, Blue Giant=628, Ring World=1275");
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>
        /// Print tools cheat sheet to console
        /// </summary>
        public static void PrintToolsCheatSheet()
        {
            Console.WriteLine(GetToolsCheatSheet());
        }

        #endregion
    }
}
