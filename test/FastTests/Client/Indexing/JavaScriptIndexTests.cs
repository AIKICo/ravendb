﻿using System;
using System.Collections.Generic;
using System.Linq;
using FastTests.Server.Documents.Indexing;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Server.Config;
using Tests.Infrastructure.Entities;
using Xunit;
using Xunit.Abstractions;
using Tests.Infrastructure;

namespace FastTests.Client.Indexing
{
    public class JavaScriptIndexTests : RavenTestBase
    {
        public JavaScriptIndexTests(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptIndex(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersByName());
                using (var session = store.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>("UsersByName").Single(x => x.Name == "Brendan Eich");
                }

            }
        }


        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanIndexTimeSpan(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new TeemoByDuration());
                using (var session = store.OpenSession())
                {
                    session.Store(new Teemo
                    {
                        Description = "5 minutes",
                        Duration = TimeSpan.FromMinutes(5)
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<Teemo>("TeemoByDuration").Single(x => x.Duration == TimeSpan.FromMinutes(5));
                }

            }
        }

        public class Teemo
        {
            public string Description { get; set; }
            public TimeSpan Duration { get; set; }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptIndexWithAdditionalSources(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersByNameWithAdditionalSources());
                using (var session = store.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>("UsersByNameWithAdditionalSources").Single(x => x.Name == "Mr. Brendan Eich");
                }

            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanIndexArrayProperties(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersByPhones());
                using (var session = store.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Jow",
                        PhoneNumbers = new[] { "555-234-8765", "555-987-3425" }
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    var result = session.Query<UsersByPhones.UsersByPhonesResult>("UsersByPhones")
                        .Where(x => x.Phone == "555-234-8765")
                        .OfType<User>()
                        .Single();
                }

            }
        }

        private class Fanout
        {
            public string Foo { get; set; }
            public int[] Numbers { get; set; }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanIndexMapWithFanout(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new FanoutByNumbers());
                using (var session = store.OpenSession())
                {
                    session.Store(new Fanout
                    {
                        Foo = "Foo",
                        Numbers = new[] { 4, 6, 11, 9 }
                    });
                    session.Store(new Fanout
                    {
                        Foo = "Bar",
                        Numbers = new[] { 3, 8, 5, 17 }
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    var result = session.Query<FanoutByNumbers.Result>("FanoutByNumbers")
                        .Where(x => x.Sum == 17)
                        .OfType<Fanout>()
                        .Single();
                    Assert.Equal("Bar", result.Foo);
                }

            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanIndexMapReduceWithFanoutWhenOutputingBlittableObjectInstance(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new FanoutByPaymentsWithReduce());
                using (var session = store.OpenSession())
                {

                    session.Store(new Customer
                    {
                        Name = "John Smidth",
                        Status = "Active",
                        Subscription = "Monthly",
                        Payments = new []
                        {
                            new DateWithAmount("2018-09-01",58),
                            new DateWithAmount("2018-10-01",48),
                            new DateWithAmount("2018-11-01",75),
                            new DateWithAmount("2018-12-01",42),
                            new DateWithAmount("2019-01-01",34)
                        }
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    
                    var res = session.Query<DateWithAmount, FanoutByPaymentsWithReduce>().Where(x => x.Amount == 42.833333333333336).ToList();
                    Assert.Equal(3, res.Count);
                }
            }
        }
        
        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanIndexMapReduceWithFanout(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new FanoutByNumbersWithReduce());
                using (var session = store.OpenSession())
                {
                    session.Store(new Fanout
                    {
                        Foo = "Foo",
                        Numbers = new[] { 4, 6, 11, 9 }
                    });
                    session.Store(new Fanout
                    {
                        Foo = "Bar",
                        Numbers = new[] { 3, 8, 5, 17 }
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    WaitForUserToContinueTheTest(store);
                    var result = session.Query<FanoutByNumbersWithReduce.Result>("FanoutByNumbersWithReduce")
                        .Where(x => x.Sum == 33)
                        .Single();
                    Assert.Equal("Bar", result.Foo);
                }

            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptIndexWithDynamicFields(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersByNameAndAnalyzedName());
                using (var session = store.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>("UsersByNameAndAnalyzedName").ProjectInto<UsersByNameAndAnalyzedName.Result>().Search(x => x.AnalyzedName, "Brendan")
                        .Single();
                }

            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptMultiMapIndex(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersAndProductsByName());
                using (var session = store.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true
                    });
                    session.Store(new Product
                    {
                        Name = "Shampoo",
                        IsAvailable = true
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>("UsersAndProductsByName").Single(x => x.Name == "Brendan Eich");
                }

            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptIndexWithLoadDocument(Options options)
        {
            CanUseJavaScriptIndexWithLoadInternal<UsersWithProductsByName>(options);
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptIndexWithExternalLoadDocument(Options options)
        {
            CanUseJavaScriptIndexWithLoadInternal<UsersWithProductsByNameWithExternalLoad>(options);
        }

        private void CanUseJavaScriptIndexWithLoadInternal<T>(Options options) where T : AbstractJavaScriptIndexCreationTask, new()
        {
            using (var store = GetDocumentStore(options))
            {
                var index = new T();
                store.ExecuteIndex(index);
                using (var session = store.OpenSession())
                {
                    var productId = "Products/1";
                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true,
                        Product = productId
                    });
                    session.Store(new Product
                    {
                        Name = "Shampoo",
                        IsAvailable = true
                    }, productId);
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>(index.IndexName).Single(x => x.Name == "Brendan Eich");
                }
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanElivateSimpleFunctions(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersByNameAndIsActive());
                using (var session = store.OpenSession())
                {

                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>("UsersByNameAndIsActive").Single(x => x.Name == "Brendan Eich" && x.IsActive == true);
                }

            }
        }
        
        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJavaScriptMapReduceIndex(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersAndProductsByNameAndCount());
                using (var session = store.OpenSession())
                {
                    session.Store(new User
                    {
                        Name = "Brendan Eich",
                        IsActive = true
                    });
                    session.Store(new Product
                    {
                        Name = "Shampoo",
                        IsAvailable = true
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    session.Query<User>("UsersAndProductsByNameAndCount").OfType<ReduceResults>().Single(x => x.Name == "Brendan Eich");
                }
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseSpatialFields(Options options)
        {
            var kalab = 10;
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new Spatial());
                CanUseSpatialFieldsInternal(kalab, store, "Spatial");
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Corax)]
        public void CanUseDynamicSpatialFields(Options options)
        {
            var kalab = 10;
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new DynamicSpatial());
                CanUseSpatialFieldsInternal(kalab, store, "DynamicSpatial");
            }
        }

        private void CanUseSpatialFieldsInternal(int kalab, DocumentStore store, string indexName)
        {
            using (var session = store.OpenSession())
            {
                session.Store(new Location
                {
                    Description = "Dor beach",
                    Latitude = 32.61059534196809,
                    Longitude = 34.918146686510454

                });
                session.Store(new Location
                {
                    Description = "Kfar Galim",
                    Latitude = 32.76724701152615,
                    Longitude = 34.957999421620116

                });
                session.SaveChanges();
                Indexes.WaitForIndexing(store);
                WaitForUserToContinueTheTest(store);
                session.Query<Location>(indexName).Spatial("Location", criteria => criteria.WithinRadius(kalab, 32.56829122491778, 34.953954053921734)).Single(x => x.Description == "Dor beach");
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanReduceNullValues(Options options)
        {
            using (var store = GetDocumentStore(options))
            using (var store2 = GetDocumentStore(options))
            {
                ReduceNullValuesInternal(store);
            }
        }

        private void ReduceNullValuesInternal(DocumentStore store)
        {
            store.ExecuteIndex(new UsersReducedByName());
            using (var session = store.OpenSession())
            {
                session.Store(new User { Name = null });
                session.Store(new User { Name = null });
                session.Store(new User { Name = null });
                session.Store(new User { Name = "Tal" });
                session.Store(new User { Name = "Maxim" });
                session.SaveChanges();
                Indexes.WaitForIndexing(store);
                var res = session.Query<User>("UsersReducedByName").OfType<ReduceResults>().Single(x => x.Count == 3);
                Assert.Null(res.Name);
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanReduceWithReturnSyntax(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersReducedByNameReturnSyntax());
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Tal" });
                    session.Store(new User { Name = "Tal" });
                    session.Store(new User { Name = "Maxim" });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                }

                using (var session = store.OpenSession())
                {
                    var res = session.Query<User>("UsersReducedByNameReturnSyntax").OfType<ReduceResults>().Single(x => x.Count == 2);
                    Assert.Equal("Tal", res.Name);
                }
            }
        }
        
        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanUseJsIndexWithArrowObjectFunctionInMap(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new UsersByNameMapArrowSyntax());
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Tal" });
                    session.Store(new User { Name = "Tal" });
                    session.Store(new User { Name = "Maxim" });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                }

                using (var session = store.OpenSession())
                {
                    var res = session
                        .Query<User, UsersByNameMapArrowSyntax>()
                        .Single(x => x.Name == "Maxim");
                    Assert.Equal("Maxim", res.Name);
                }
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void IdenticalMapReduceIndexWillGenerateDiffrentIndexInstance(Options options)
        {
            using (var store = GetDocumentStore(options))
            using (var store2 = GetDocumentStore(options))
            {
                ReduceNullValuesInternal(store);
                ReduceNullValuesInternal(store2);
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void OutputReduceToCollection(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                OutputReduceToCollectionAssertion(store);
            }
        }
        
        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void OutputReduceToCollectionWithDeletions(Options options)
        {
            using var store = GetDocumentStore(options);
            OutputReduceToCollectionAssertion(store);
            {
                using var session = store.OpenSession();
                session.Delete("categories/1-A");
                var item = session.Query<Product>().Where(i => i.Name == "Lakkalikööri").Single();
                session.Delete(item);
                session.SaveChanges();
            }
            Indexes.WaitForIndexing(store);

            {
                using var session = store.OpenSession();
                var res = session.Query<Products_ByCategory.Result>("Products/ByCategory")
                    .ToList();
                var res2 = session.Query<CategoryCount>()
                    .ToList();
                Assert.Equal(res.Count, res2.Count);
            }
        }

        private void OutputReduceToCollectionAssertion(IDocumentStore store)
        {
            store.ExecuteIndex(new Products_ByCategory());
            using (var session = store.OpenSession())
            {
                session.Store(new Category { Name = "Beverages" }, "categories/1-A");
                session.Store(new Category { Name = "Seafood" }, "categories/2-A");
                session.Store(new Product { Name = "Lakkalikööri", Category = "categories/1-A", PricePerUnit = 13 });
                session.Store(new Product { Name = "Original Frankfurter", Category = "categories/1-A", PricePerUnit = 16 });
                session.Store(new Product { Name = "Röd Kaviar", Category = "categories/2-A", PricePerUnit = 18 });
                session.SaveChanges();
                Indexes.WaitForIndexing(store);
                var res = session.Query<Products_ByCategory.Result>("Products/ByCategory")
                    .ToList();
                var res2 = session.Query<CategoryCount>()
                    .ToList();
                Assert.Equal(res.Count, res2.Count);
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Corax)]
        public void DateCheckMapReduce(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new Product_Sales_ByMonth());
                using (var session = store.OpenSession())
                {
                    session.Store(new Order
                    {
                        Lines = new List<OrderLine>
                        {
                            new OrderLine() {ProductName = "Chang"},
                            new OrderLine() {ProductName = "Aniseed Syrup"},
                            new OrderLine() {ProductName = "Chef Anton's Cajun Seasoning"}
                        },
                        OrderedAt = new DateTime(1998, 5, 6)
                    });
                    session.Store(new Order
                    {
                        Lines = new List<OrderLine>
                        {
                            new OrderLine() {ProductName = "Grandma's Boysenberry Spread"},
                            new OrderLine() {ProductName = "Tofu"},
                            new OrderLine() {ProductName = "Teatime Chocolate Biscuits"}
                        },
                        OrderedAt = new DateTime(1998, 5, 10)
                    });
                    session.Store(new Order
                    {
                        Lines = new List<OrderLine>
                        {
                            new OrderLine() {ProductName = "Guaraná Fantástica"},
                            new OrderLine() {ProductName = "NuNuCa Nuß-Nougat-Creme"},
                            new OrderLine() {ProductName = "Perth Pasties"},
                            new OrderLine() {ProductName = "Outback Lager"}
                        },
                        OrderedAt = new DateTime(1998, 4, 30)
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    WaitForUserToContinueTheTest(store);
                    var res = session.Query<Product_Sales_ByMonth.Result>("Product/Sales/ByMonth")
                        .Where(x => x.Count == 6)
                        .ToList();
                    Assert.Equal(res[0].Month.Month, 5);
                }
            }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Corax, Skip = "Complex field")]
        public void CanQueryBySubObjectAsString(Options options)
        {
            var address = new Address
            {
                Line1 = "Home",
                Line2 = "sweet home"
            };

            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new Users_ByAddress());
                using (var session = store.OpenSession())
                {
                    session.Store(new User { Name = "Foo", Address = address });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    WaitForUserToContinueTheTest(store);
                    var user = session.Query<User>("Users/ByAddress").Single(u => u.Address == address);
                    Assert.Equal("Foo", user.Name);
                }
            }
        }
        private class ProductsWarrentyResult
        {
            public string Warranty { get; set; }
            public int Duration { get; set; }
        }

        [RavenTheory(RavenTestCategory.JavaScript | RavenTestCategory.Indexes)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
        public void CanIndexSwitchCases(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                store.ExecuteIndex(new ProductsWarrenty());
                using (var session = store.OpenSession())
                {
                    session.Store(new Product
                    {
                        Name = "p1",
                        Mode = "Used",
                        Manufacturer = "ACME",
                        Type = 1
                    });
                    session.Store(new Product
                    {
                        Name = "p2",
                        Mode = "Refurbished",
                        Manufacturer = "ACME",
                        Type = 1
                    });
                    session.Store(new Product
                    {
                        Name = "p3",
                        Mode = "New",
                        Manufacturer = "ACME",
                        Type = 2
                    });
                    session.Store(new Product
                    {
                        Name = "p4",
                        Mode = "New",
                        Manufacturer = "EMCA",
                        Type = 2
                    });
                    session.Store(new Product
                    {
                        Name = "p5",
                        Mode = "Refurbished",
                        Manufacturer = "EMCA",
                        Type = 2
                    });
                    session.SaveChanges();
                    Indexes.WaitForIndexing(store);
                    var res = session.Query<ProductsWarrentyResult>("ProductsWarrenty").Where(u => u.Duration > 20).ProjectInto<Product>().Single();
                    Assert.Equal(res.Name, "p3");
                }
            }
        }

        private class Category
        {
            public string Description { get; set; }
            public string Name { get; set; }
        }

        private class CategoryCount
        {
            public string Category { get; set; }

            public int Count { get; set; }
        }

        private class User
        {
            public string Name { get; set; }
            public bool IsActive { get; set; }
            public string Product { get; set; }
            public string[] PhoneNumbers { get; set; }
            public Address Address { get; set; }
        }

        private class Address
        {
            public string Line1 { get; set; }
            public string Line2 { get; set; }
        }
        private class Product
        {
            public string Name { get; set; }
            public bool IsAvailable { get; set; }
            public string Category { get; set; }
            public int PricePerUnit { get; set; }
            public string Mode { get; set; }
            public int Type { get; set; }
            public string Manufacturer { get; set; }
        }

        private class ReduceResults
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }

        private class UsersByName : AbstractJavaScriptIndexCreationTask
        {
            public UsersByName()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Count: 1};})",
                };
            }
        }

        private class TeemoByDuration : AbstractJavaScriptIndexCreationTask
        {
            public TeemoByDuration()
            {
                Maps = new HashSet<string>
                {
                    @"map('Teemos', function (t){ return { Duration: t.Duration, Description: t.Description};})",
                };
            }
        }

        private class UsersByNameWithAdditionalSources : AbstractJavaScriptIndexCreationTask
        {
            public UsersByNameWithAdditionalSources()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: Mr(u.Name)};})",
                };
                AdditionalSources = new Dictionary<string, string>
                {
                    ["The Script"] = @"function Mr(x){
                                        return 'Mr. ' + x;
                                        }"
                };

            }

        }

        private class FanoutByNumbers : AbstractJavaScriptIndexCreationTask
        {
            public FanoutByNumbers()
            {
                Maps = new HashSet<string>
                {
                    @"map('Fanouts', function (f){
                                var result = [];
                                for(var i = 0; i < f.Numbers.length; i++)
                                {
                                    result.push({
                                        Foo: f.Foo,
                                        Sum: f.Numbers[i]
                                    });
                                 }
                                return result;
                            })",
                };
            }

            internal class Result
            {
                public string Foo { get; set; }
                public int Sum { get; set; }
            }
        }

        private class FanoutByNumbersWithReduce : AbstractJavaScriptIndexCreationTask
        {
            public FanoutByNumbersWithReduce()
            {
                Maps = new HashSet<string>
                {
                    @"map('Fanouts', function (f){
                                var result = [];
                                for(var i = 0; i < f.Numbers.length; i++)
                                {
                                    result.push({
                                        Foo: f.Foo,
                                        Sum: f.Numbers[i]
                                    });
                                }
                                return result;
                                })",
                };
                Reduce = @"groupBy( f => f.Foo )
                             .aggregate( g => ({
                                 Foo: g.key,
                                 Sum: g.values.reduce((total, val) => val.Sum + total,0)
                             }))";
            }
            internal class Result
            {
                public string Foo { get; set; }
                public int Sum { get; set; }
            }
        }

        private class Customer
        {
            public string Name { get; set; }
            public string Status { get; set; }
            public string Subscription { get; set; }
            public DateWithAmount[] Payments { get; set; }
        }

        private class DateWithAmount
        {
            public DateWithAmount(string date, double amount)
            {
                Date = date;
                Amount = amount;
            }
            public string Date { get; set; }
            public double Amount { get; set; }
        }

        private class FanoutByPaymentsWithReduce : AbstractJavaScriptIndexCreationTask
        {
            public FanoutByPaymentsWithReduce()
            {
                Maps = new HashSet<string>
                {
                    @"map('Customers', 
                    cust =>{ var length = cust.Payments.length;
                    if (length == 0){
                        return; // nothing to work on
                        }
                    var res = [];
                    var lastPayment = new Date(cust.Payments[length - 1].Date);
                    
                    for (var t = 0; t < 3; t++)
                    {
                        for (var i = 1, sum = 0; i <= length; i++)
                        {
                            sum += cust.Payments[length - i].Amount;
                        }
                    
                        if (cust.Subscription == 'Monthly')
                        {
                            lastPayment.setMonth(lastPayment.getMonth() + 1);                        
                        }
                        else
                        {
                            lastPayment.setYear(lastPayment.getYear() + 1);
                        }

                        res.push(
                            { 
                                Amount: sum / i, 
                                Date: lastPayment.toISOString().substr(0, 10)
                            });
                    }
                    return res;
                    })"
                    };

                Reduce = @"groupBy(x=>x.Date)
                            .aggregate(g=>{ 
                                            return {
                                                    Date: g.key,
                                                    Amount: g.values.reduce((c,v)=>c+v.Amount,0)
                                                    };
                                          });";
            }
        }

        private class UsersByPhones : AbstractJavaScriptIndexCreationTask
        {
            public class UsersByPhonesResult
            {
                public string Name { get; set; }
                public string Phone { get; set; }
            }

            public UsersByPhones()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Phone: u.PhoneNumbers};})",
                };
            }
        }

        private class UsersByNameAndAnalyzedName : AbstractJavaScriptIndexCreationTask
        {
            public UsersByNameAndAnalyzedName()
            {
                Maps = new HashSet<string>
                        {
                            @"map('Users', function (u){
                                    return {
                                        Name: u.Name,
                                        _: {$value: u.Name, $name:'AnalyzedName', $options:{indexing: 'Search', storage: true}}
                                    };
                                })",
                        };
                Fields = new Dictionary<string, IndexFieldOptions>
                        {
                            {
                                Constants.Documents.Indexing.Fields.AllFields, new IndexFieldOptions()
                                {
                                    Indexing = FieldIndexing.Search,
                                    Analyzer = "StandardAnalyzer"
                                }
                            }
                        };
            }
            public class Result
            {
                public string AnalyzedName { get; set; }
            }
        }

        private class UsersAndProductsByName : AbstractJavaScriptIndexCreationTask
        {
            public UsersAndProductsByName()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Count: 1};})",
                    @"map('Products', function (p){ return { Name: p.Name, Count: 1};})"
                };
            }
        }

        private class UsersByNameAndIsActive : AbstractJavaScriptIndexCreationTask
        {
            public UsersByNameAndIsActive()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', u => u.Name, function(f){ return f.IsActive;})",
                };
            }
        }

        private class UsersWithProductsByName : AbstractJavaScriptIndexCreationTask
        {
            public UsersWithProductsByName()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Count: 1, Product: load(u.Product,'Products').Name};})",
                };
            }
        }

        private class UsersWithProductsByNameWithExternalLoad : AbstractJavaScriptIndexCreationTask
        {
            public UsersWithProductsByNameWithExternalLoad()
            {
                Maps = new HashSet<string>
                {
                    @"
function GetProductName(u){
    return load(u.Product,'Products').Name
}
map('Users', function (u){ return { Name: u.Name, Count: 1, Product: GetProductName(u)};})",
                };
            }
        }

        private class ProductsWarrenty : AbstractJavaScriptIndexCreationTask
        {
            public ProductsWarrenty()
            {
                Maps = new HashSet<string>
                {
                    @"
map('Products', prod => {
                    var result = { Warranty: 'Parts', Duration: 1 };
                if (prod.Mode == 'Used')
                    return result;
                switch (prod.Type)
                {
                    case 1:
                        return null;
                }
                if (prod.Manufacturer == 'ACME')
                {  // our product
                    result.Warranty = 'Full';
                    result.Duration = 24;
                }
                else
                { // 3rd party
                    result.Warranty = 'Parts';
                    result.Duration = 6;
                }

                if (prod.Mode == Refurbished)
                    result.Duration /= 2;

                return result;
                });"
                };
            }
        }
        private class Location
        {
            public string Description { get; set; }
            public double Longitude { get; set; }
            public double Latitude { get; set; }
        }

        private class Spatial : AbstractJavaScriptIndexCreationTask
        {
            public Spatial()
            {
                Maps = new HashSet<string>
                {
                    @"map('Locations', function (l){ return { Description: l.Description, Location: createSpatialField(l.Latitude, l.Longitude)}})",
                };
            }
        }

        private class DynamicSpatial : AbstractJavaScriptIndexCreationTask
        {
            public DynamicSpatial()
            {
                Maps = new HashSet<string>
                {
                    @"map('Locations', function (l){ return { Description: l.Description, _:{$value: createSpatialField(l.Latitude, l.Longitude), $name:'Location', $options:{indexing: 'Search', storage: true}} }})",
                };
            }
        }

        private class UsersAndProductsByNameAndCount : AbstractJavaScriptIndexCreationTask
        {
            public UsersAndProductsByNameAndCount()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Count: 1};})",
                    @"map('Products', function (p){ return { Name: p.Name, Count: 1};})"
                };
                Reduce = @"groupBy( x =>  x.Name )
                                .aggregate(g => {return {
                                    Name: g.key,
                                    Count: g.values.reduce((total, val) => val.Count + total,0)
                               };})";
            }

        }

        private class UsersReducedByName : AbstractJavaScriptIndexCreationTask
        {
            public UsersReducedByName()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Count: 1};})",
                };
                Reduce = @"groupBy( x =>  x.Name )
                                .aggregate(g => {return {
                                    Name: g.key,
                                    Count: g.values.reduce((total, val) => val.Count + total,0)
                               };})";

            }
        }
        
        private class UsersReducedByNameReturnSyntax : AbstractJavaScriptIndexCreationTask
        {
            public UsersReducedByNameReturnSyntax()
            {
                Maps = new HashSet<string>
                {
                    @"map('Users', function (u){ return { Name: u.Name, Count: 1 };})",
                };
                
                Reduce = @"groupBy(x => { return { Name: x.Name } })
                                .aggregate(g => {return {
                                    Name: g.key.Name,
                                    Count: g.values.reduce((total, val) => val.Count + total,0)
                               };})";

            }
        }
        
        private class UsersByNameMapArrowSyntax : AbstractJavaScriptIndexCreationTask
        {
            public UsersByNameMapArrowSyntax()
            {
                // using arrow function w/o explicit return statement
                Maps = new HashSet<string>
                {
                    @"map('Users', u => ({ Name: u.Name }))",
                };
            }
        }

        public class Products_ByCategory : AbstractJavaScriptIndexCreationTask
        {
            public class Result
            {
                public string Category { get; set; }

                public int Count { get; set; }
            }

            public Products_ByCategory()
            {
                Maps = new HashSet<string>()
                {
                    @"map('products', function(p){
                        return {
                            Category:
                            load(p.Category, 'Categories').Name,
                            Count:
                            1
                        }
                    })"
                };

                Reduce = @"groupBy( x => x.Category )
                            .aggregate(g => {
                                return {
                                    Category: g.key,
                                    Count: g.values.reduce((count, val) => val.Count + count, 0)
                               };})";

                OutputReduceToCollection = "CategoryCounts";
            }
        }

        public class Product_Sales_ByMonth : AbstractJavaScriptIndexCreationTask
        {
            public class Result
            {
                public string Product { get; set; }

                public DateTime Month { get; set; }

                public int Count { get; set; }

            }

            public Product_Sales_ByMonth()
            {
                Maps = new HashSet<string>()
                {
                    @"map('orders', function(order){
                            var res = [];
                            order.Lines.forEach(l => {
                                res.push({
                                    Product: l.Product,
                                    Month: new Date( (new Date(order.OrderedAt)).getFullYear(),(new Date(order.OrderedAt)).getMonth(),1),
                                    Count: 1,
                                })
                            });
                            return res;
                        })"
                };

                Reduce = @"groupBy( x => ({Product: x.Product , Month: x.Month}) )
                    .aggregate(g => {
                    return {
                        Product: g.key.Product,
                        Month: g.key.Month,
                        Count: g.values.reduce((sum, x) => x.Count + sum, 0)
                    }
                })";

            }
        }

        public class Users_ByAddress : AbstractJavaScriptIndexCreationTask
        {


            public Users_ByAddress()
            {
                Maps = new HashSet<string>
                {
                    @"map('users', function(u){
                            return {Address: u.Address}
                        })"
                };

            }
        }
    }

}
