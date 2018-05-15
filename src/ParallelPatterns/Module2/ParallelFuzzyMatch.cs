﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using FuzzyMatch;
using Microsoft.FSharp.Core;
using ParallelPatterns.Common;
using ParallelPatterns.Common.TaskEx;
using ParallelPatterns.Fsharp;
using ParallelPatterns.TaskComposition;
using static FuzzyMatch.JaroWinklerModule.FuzyMatchStructures;
using static ParallelPatterns.Common.FuzzyMatchHelpers;

namespace ParallelPatterns
{
    public partial class ParallelFuzzyMatch
    {
        public static void RunFuzzyMatchPipeline(string[] wordsLookup,
            IEnumerable<string> files)
        {
            // TODO (3) : In the previous example you have implemented the Monadic operator SelectMany (usually called Bind) 
            // This operator enables the compiler to understand the monadi (LINQ) pattern, which allows you to write
            // expressive/declarative code in LINQ style
            // Let's implement a parallel Pipeline that allows you to keep the continuation semantic, with the advantage of running 
            // the transoramations in parallel
            //
            // Implement the "Then" operator (instance method) that can be used to create and fluently compose a pipeline 
            // for example:
            // pipeline.Then( .... ).Then(...)
            // 
            // Also implement the logic in the "Enqueue" method 
            // 
            // C# : go to "Module 2\Pipeline.cs" and add the missing code (3.a and 3.b)
            // F# : go to the FSharp project "Module 2\Pipeline.fs" and add the missing code (3.a and 3.b)
            //
            // When you are complete, uncomment the code and run it

            // TODO (3) Start C# Pipeline 
            var pipeline = Pipeline<string, string[]>.Create(file => File.ReadAllLinesAsync(file));

            pipeline.Then(lines =>
                {
                    return lines.SelectMany(l => l.Split(punctuation.Value)
                        .Where(w => !IgnoreWords.Contains(w))).AsSet();
                })
                .Then(wordSet =>
                {
                    return wordsLookup.Traverse(wl => JaroWinklerModule.bestMatchTask(wordSet, wl, threshold));
                })
                .Then(matcheSet => PrintSummary(matcheSet.Flatten().AsSet()));

            foreach (var file in files)
            {
                Console.WriteLine($"analyzing file {file}");
                pipeline.Engueue(file);
            }
            // End C# Pipeline
            
            
            // TODO (3) Start F# Pipeline 
            var pipelineFSharp =
                Pipeline.Pipeline<string, string[]>.Create(file => File.ReadAllLinesAsync(file))
                    .Then(lines =>
                    {
                        return lines.SelectMany(l => l.Split(punctuation.Value)
                            .Where(w => !IgnoreWords.Contains(w))).AsSet();
                    })
                    .Then(wordSet =>
                    {
                        return wordsLookup.Traverse(wl => JaroWinklerModule.bestMatchTask(wordSet, wl, threshold));
                    })
                    .Then(matcheSet => matcheSet.Flatten().AsSet());
          
            pipelineFSharp.Execute(4, CancellationToken.None);

            foreach (var file in files)
            {
                pipelineFSharp.Enqueue(file,
                    ((tup) =>
                    {
                        Console.WriteLine($"analyzing file {file}");
                        PrintSummary(tup.Item2);
                        return (Unit) Activator.CreateInstance(typeof(Unit), true);
                    }));
            }
            // End F# Pipeline 

        }


        public static async Task<IDictionary<string, HashSet<string>>>
            RunFuzzyMatchTaskProcessAsCompleteBasic(string[] wordsLookup, IEnumerable<string> files)
        {
            // An alternative pattern to parallize the FuzzyMatch is the "Procces as complete"
            // The idea of this pattern is to start the execution of all the operations (tasks) 
            // at the same time, and then proccess them as they complete instead of waiting for all the operations
            // to be completed before continuing.
            // In other words, this pattern returns a sequence of tasks which will be observed to complete with the same set 
            // of results as the given input tasks, but in the order in which the original tasks complete.
            //
            // Here a simple implementation ::

            var matcheSet = new HashSet<WordDistanceStruct>();

            var readFileTasks = (from file in files select File.ReadAllTextAsync(file)).ToList();

            while (readFileTasks.Count > 0)
            {
                await Task.WhenAny(readFileTasks)
                    .ContinueWith(async readtsk =>
                    {
                        var finishedReadTask = readtsk.Result;
                        readFileTasks.Remove(finishedReadTask);

                        var words = WordRegex.Value.Split(finishedReadTask.Result)
                            .Where(w => !IgnoreWords.Contains(w));

                        var matchTasks = (from wl in wordsLookup
                            select JaroWinklerModule.bestMatchTask(words, wl, threshold)).ToList();

                        while (matchTasks.Count > 0)
                        {
                            await Task.WhenAny(matchTasks).ContinueWith(matchtsk =>
                            {
                                var finishedMatchTask = matchtsk.Result;
                                matchTasks.Remove(finishedMatchTask);
                                matcheSet.AddRange(finishedMatchTask.Result);
                            });
                        }
                    });
            }

            return PrintSummary(matcheSet);
        }


        public static async Task<IDictionary<string, HashSet<string>>>
            RunFuzzyMatchTaskProcessAsCompleteAbstracted(string[] wordsLookup, IEnumerable<string> files)
        {
            var matcheSet = new HashSet<WordDistanceStruct>();

            // TODO (4) : Implement a resuable function called "ContinueAsComplete" to abstract the implementation of the 
            // previous method "RunFuzzyMatchTaskProcessAsCompleteBasic".
            // The function "ContinueAsComplete" should sattisfy the following signatue:
            // Signature :
            // Enumerable<Task<R>> ContinueAsComplete<T, R>(this IEnumerable<T> input, Func<T, Task<R>> selector)
            // 
            // C# : go to "Module 2\TaskAsComplete.cs" and add the missing code (4)
            // F# : go to the FSharp project "Module 2\TaskAsComplete.fs" and add the missing code (4)

            foreach (var textTask in files.ContinueAsComplete(file => File.ReadAllTextAsync(file)))
            {
                var text = await textTask;

                var words = WordRegex.Value.Split(text)
                    .Where(w => !IgnoreWords.Contains(w))
                    .AsSet();

                foreach (var matchTask in wordsLookup.ContinueAsComplete(
                    wl => JaroWinklerModule.bestMatchTask(words, wl, threshold)))
                {
                    var match = await matchTask;
                    matcheSet.AddRange(match);
                }
            }

            return PrintSummary(matcheSet);
        }
    }
}