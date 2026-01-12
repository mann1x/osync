namespace osync
{
    /// <summary>
    /// Coding-focused test suite v1code with 50 questions across 5 programming languages
    /// Uses double the default token output (8192) for longer code responses
    /// </summary>
    public class V1CodeTestSuite : ITestSuite
    {
        public string Name => "v1code";

        public int TotalQuestions => 50;

        public int NumPredict => 8192;

        public int ContextLength => 8192;

        public List<TestCategory> GetCategories()
        {
            return new List<TestCategory>
            {
                CreatePythonCategory(),
                CreateCppCategory(),
                CreateCSharpCategory(),
                CreateTypeScriptCategory(),
                CreateRustCategory()
            };
        }

        private TestCategory CreatePythonCategory()
        {
            return new TestCategory
            {
                Id = 1,
                Name = "Python",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 1,
                        Text = "Implement a Python class for a thread-safe LRU (Least Recently Used) cache with O(1) time complexity for both get and put operations. Include proper locking mechanisms and support for a configurable maximum size. Provide the complete implementation with docstrings. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 2,
                        Text = "Write a Python async web scraper using aiohttp that can crawl multiple URLs concurrently, respect rate limits, handle retries with exponential backoff, and extract structured data using CSS selectors. Include error handling and logging. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 3,
                        Text = "Implement a Python decorator factory that creates decorators for automatic retry logic with configurable max attempts, delay strategy (fixed, exponential, jitter), and exception filtering. Include support for both sync and async functions. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 4,
                        Text = "Create a Python implementation of a B-tree data structure with configurable order, supporting insert, delete, search, and range query operations. Include proper node splitting and merging logic. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 5,
                        Text = "Write a Python metaclass that automatically generates __init__, __repr__, __eq__, and __hash__ methods for dataclass-like classes, with support for inheritance, default values, and type validation at runtime. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 6,
                        Text = "Implement a Python coroutine-based event loop from scratch that supports scheduling callbacks, timers, and I/O multiplexing using select/poll. Demonstrate with a simple echo server. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 7,
                        Text = "Create a Python implementation of the A* pathfinding algorithm for a weighted graph with support for custom heuristics, diagonal movement options, and obstacle handling. Include visualization of the path. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 8,
                        Text = "Write a Python parser for a simple expression language supporting arithmetic operations, variables, function calls, and conditionals using recursive descent parsing. Include lexer and AST representation. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 9,
                        Text = "Implement a Python connection pool for database connections with configurable min/max connections, health checks, connection timeout, and automatic reconnection. Support context manager usage. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 1,
                        QuestionId = 10,
                        Text = "Create a Python implementation of consistent hashing for distributed caching with virtual nodes, node addition/removal, and key migration tracking. Include load balancing metrics. Keep your response under 8000 tokens."
                    }
                }
            };
        }

        private TestCategory CreateCppCategory()
        {
            return new TestCategory
            {
                Id = 2,
                Name = "C++",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 1,
                        Text = "Implement a C++ lock-free queue using atomic operations and memory ordering constraints. Support multiple producers and multiple consumers with proper memory barrier usage. Include move semantics. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 2,
                        Text = "Write a C++ smart pointer implementation similar to shared_ptr with custom deleter support, weak_ptr functionality, thread-safe reference counting, and proper handling of incomplete types. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 3,
                        Text = "Create a C++ template metaprogramming library for compile-time type list manipulation including map, filter, fold, reverse, and unique operations. Demonstrate with practical examples. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 4,
                        Text = "Implement a C++ memory allocator with a free list, coalescing of adjacent free blocks, and alignment support. Include statistics tracking and fragmentation metrics. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 5,
                        Text = "Write a C++ coroutine-based task system using C++20 coroutines with support for co_await, task chaining, exception propagation, and cancellation tokens. Include an executor. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 6,
                        Text = "Create a C++ compile-time regular expression engine using constexpr and template metaprogramming that can match patterns against string literals at compile time. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 7,
                        Text = "Implement a C++ thread pool with work stealing, task priorities, and affinity hints. Support both detached and joinable tasks with future-based result retrieval. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 8,
                        Text = "Write a C++ RAII-based resource management system with support for multiple resource types, automatic cleanup ordering based on dependencies, and exception-safe acquisition. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 9,
                        Text = "Create a C++ implementation of a skip list with template support for custom comparators, iterators, and concurrent read access. Include performance comparison with std::map. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 2,
                        QuestionId = 10,
                        Text = "Implement a C++ serialization framework using reflection-like techniques with macros or concepts, supporting binary and JSON formats, versioning, and forward/backward compatibility. Keep your response under 8000 tokens."
                    }
                }
            };
        }

        private TestCategory CreateCSharpCategory()
        {
            return new TestCategory
            {
                Id = 3,
                Name = "CSharp",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 1,
                        Text = "Implement a C# source generator that automatically generates builder pattern classes for any class decorated with a custom attribute. Include support for required properties and validation. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 2,
                        Text = "Write a C# implementation of the actor model using System.Threading.Channels, supporting typed messages, supervision strategies, and actor lifecycle management. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 3,
                        Text = "Create a C# expression tree visitor that transforms LINQ queries into SQL statements, supporting joins, grouping, ordering, and parameterized queries with SQL injection prevention. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 4,
                        Text = "Implement a C# middleware pipeline similar to ASP.NET Core's, supporting async middleware, short-circuiting, dependency injection, and request/response modification. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 5,
                        Text = "Write a C# implementation of a Trie (prefix tree) with support for autocomplete suggestions, fuzzy matching with edit distance, and memory-efficient storage using compressed nodes. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 6,
                        Text = "Create a C# reactive extensions implementation with Observable, Observer, and operators like Map, Filter, Merge, Throttle, and Retry. Include proper disposal and error handling. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 7,
                        Text = "Implement a C# dependency injection container from scratch supporting constructor injection, property injection, scoped/transient/singleton lifetimes, and circular dependency detection. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 8,
                        Text = "Write a C# implementation of the Saga pattern for distributed transactions with compensating actions, timeout handling, and persistent state. Include an example e-commerce order flow. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 9,
                        Text = "Create a C# rate limiter using the token bucket algorithm with support for multiple policies, distributed state using Redis, and sliding window fallback. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 3,
                        QuestionId = 10,
                        Text = "Implement a C# object pool with automatic size management, health checks, and async borrow/return operations. Support for IDisposable objects and configurable eviction policies. Keep your response under 8000 tokens."
                    }
                }
            };
        }

        private TestCategory CreateTypeScriptCategory()
        {
            return new TestCategory
            {
                Id = 4,
                Name = "TypeScript",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 1,
                        Text = "Implement a TypeScript type-safe event emitter with generic event maps, proper inference for event handlers, and support for once listeners and async handlers. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 2,
                        Text = "Write a TypeScript implementation of a state machine with type-safe transitions, guards, actions, and nested states. Include visualization of the state graph. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 3,
                        Text = "Create a TypeScript validation library using branded types and template literal types for schema definition, supporting nested objects, arrays, unions, and custom validators. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 4,
                        Text = "Implement a TypeScript Redux-like store with middleware support, time-travel debugging, and automatic TypeScript inference for actions and selectors. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 5,
                        Text = "Write a TypeScript query builder with fluent API, type-safe column references, automatic join inference, and support for subqueries and CTEs. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 6,
                        Text = "Create a TypeScript dependency injection system using decorators and reflect-metadata, supporting lazy initialization, scopes, and automatic interface-to-implementation binding. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 7,
                        Text = "Implement a TypeScript virtual DOM diffing algorithm with efficient reconciliation, keyed children handling, and batched updates. Include a simple component system. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 8,
                        Text = "Write a TypeScript GraphQL client with automatic type generation from schema, query caching, optimistic updates, and subscription support. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 9,
                        Text = "Create a TypeScript promise-based worker pool for CPU-intensive tasks, with type-safe message passing, automatic worker recycling, and task prioritization. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 4,
                        QuestionId = 10,
                        Text = "Implement a TypeScript router with type-safe route parameters, nested routes, guards, lazy loading, and automatic breadcrumb generation. Keep your response under 8000 tokens."
                    }
                }
            };
        }

        private TestCategory CreateRustCategory()
        {
            return new TestCategory
            {
                Id = 5,
                Name = "Rust",
                Questions = new List<TestQuestion>
                {
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 1,
                        Text = "Implement a Rust async runtime from scratch with a basic executor, waker implementation, and timer support. Demonstrate with a simple async TCP echo server. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 2,
                        Text = "Write a Rust lock-free concurrent hash map using atomic operations with support for resize, iterators, and entry API. Include proper memory reclamation. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 3,
                        Text = "Create a Rust procedural macro for deriving a builder pattern with support for required fields, default values, and validation. Include compile-time error messages. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 4,
                        Text = "Implement a Rust memory arena allocator with typed allocations, automatic drop handling, and support for self-referential structures using Pin. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 5,
                        Text = "Write a Rust parser combinator library with support for recursive grammars, error recovery, and source location tracking. Include common parsers and demonstrate with JSON parsing. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 6,
                        Text = "Create a Rust implementation of Software Transactional Memory (STM) with support for nested transactions, retry, and conflict detection. Demonstrate with a concurrent bank account example. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 7,
                        Text = "Implement a Rust ECS (Entity Component System) with archetypal storage, parallel query execution, and change detection. Include a simple game loop example. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 8,
                        Text = "Write a Rust futures-based channel implementation with bounded and unbounded variants, select! macro support, and backpressure handling. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 9,
                        Text = "Create a Rust implementation of a rope data structure for efficient text editing, supporting insert, delete, and index operations with O(log n) complexity. Include iterator support. Keep your response under 8000 tokens."
                    },
                    new TestQuestion
                    {
                        CategoryId = 5,
                        QuestionId = 10,
                        Text = "Implement a Rust compile-time state machine using the typestate pattern with enforced valid transitions, impossible states being unrepresentable, and zero runtime overhead. Keep your response under 8000 tokens."
                    }
                }
            };
        }
    }
}
