﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RSG.Utils
{
    /// <summary>
    /// Implements a non-generic C# promise, this is a promise that simply resolves without delivering a value.
    /// https://developer.mozilla.org/en/docs/Web/JavaScript/Reference/Global_Objects/Promise
    /// </summary>
    public interface IPromise
    {
        /// <summary>
        /// Catch any execption that is thrown while the promise is being resolved.
        /// </summary>
        IPromise Catch(Action<Exception> onError);

        /// <summary>
        /// Handle completion of the promise.
        /// </summary>
        IPromise Done(Action onCompleted);

        /// <summary>
        /// Chains another asynchronous operation. 
        /// May also change the type of value that is being fulfilled.
        /// </summary>
        IPromise Then(Func<IPromise> chain);
    }

    /// <summary>
    /// Interface for a promise that can be rejected or resolved.
    /// </summary>
    public interface IPendingPromise
    {
        /// <summary>
        /// Reject the promise with an exception.
        /// </summary>
        void Reject(Exception ex);

        /// <summary>
        /// Resolve the promise with a particular value.
        /// </summary>
        void Resolve();
    }

    /// <summary>
    /// Implements a non-generic C# promise, this is a promise that simply resolves without delivering a value.
    /// https://developer.mozilla.org/en/docs/Web/JavaScript/Reference/Global_Objects/Promise
    /// </summary>
    public class Promise : IPromise, IPendingPromise
    {
        /// <summary>
        /// The exception when the promise is rejected.
        /// </summary>
        private Exception rejectionException;

        /// <summary>
        /// Error handlers.
        /// </summary>
        private List<Action<Exception>> errorHandlers;

        /// <summary>
        /// Completed handlers that accept no value.
        /// </summary>
        private List<Action> completedHandlers;

        /// <summary>
        /// Tracks the current state of the promise.
        /// </summary>
        public PromiseState CurState { get; private set; }

        public Promise()
        {
            this.CurState = PromiseState.Pending;
        }

        /// <summary>
        /// Helper function clear out all handlers after resolution or rejection.
        /// </summary>
        private void ClearHandlers()
        {
            errorHandlers = null;
            completedHandlers = null;
        }

        /// <summary>
        /// Reject the promise with an exception.
        /// </summary>
        public void Reject(Exception ex)
        {
            Argument.NotNull(() => ex);

            if (CurState != PromiseState.Pending)
            {
                throw new ApplicationException("Attempt to reject a promise that is already in state: " + CurState + ", a promise can only be rejected when it is still in state: " + PromiseState.Pending);
            }

            rejectionException = ex;

            CurState = PromiseState.Rejected;

            if (errorHandlers != null)
            {
                errorHandlers.Each(handler => handler(rejectionException));
            }

            ClearHandlers();
        }


        /// <summary>
        /// Resolve the promise with a particular value.
        /// </summary>
        public void Resolve()
        {
            if (CurState != PromiseState.Pending)
            {
                throw new ApplicationException("Attempt to resolve a promise that is already in state: " + CurState + ", a promise can only be resolved when it is still in state: " + PromiseState.Pending);
            }

            CurState = PromiseState.Resolved;

            if (completedHandlers != null)
            {
                completedHandlers.Each(handler => handler());
            }
            
            ClearHandlers();
        }

        /// <summary>
        /// Catch any execption that is thrown while the promise is being resolved.
        /// </summary>
        public IPromise Catch(Action<Exception> onError)
        {
            Argument.NotNull(() => onError);

            if (CurState == PromiseState.Pending)
            {
                // Promise is in flight, queue handler for possible call later.
                if (errorHandlers == null)
                {
                    errorHandlers = new List<Action<Exception>>();
                }

                errorHandlers.Add(onError);
            }
            else if (CurState == PromiseState.Rejected)
            {
                // Promise has already been rejected, immediately call handler.
                onError(rejectionException);
            }

            return this;
        }

        /// <summary>
        /// Handle completion of the promise.
        /// </summary>
        public IPromise Done(Action onCompleted)
        {
            Argument.NotNull(() => onCompleted);

            if (CurState == PromiseState.Pending)
            {
                // Promise is in flight, queue handler for possible call later.
                if (completedHandlers == null)
                {
                    completedHandlers = new List<Action>();
                }
                completedHandlers.Add(onCompleted);
            }
            else if (CurState == PromiseState.Resolved)
            {
                // Promise has already been resolved, immediately call handler.
                onCompleted();
            }

            return this;
        }

        /// <summary>
        /// Chains another asynchronous operation. 
        /// May also change the type of value that is being fulfilled.
        /// </summary>
        public IPromise Then(Func<IPromise> chain)
        {
            Argument.NotNull(() => chain);

            var resultPromise = new Promise();
            
            Catch(e => resultPromise.Reject(e));
            Done(() =>
            {
                try
                {
                    var chainedPromise = chain();
                    chainedPromise.Catch(e => resultPromise.Reject(e));
                    chainedPromise.Done(() => resultPromise.Resolve());
                }
                catch (Exception ex)
                {
                    resultPromise.Reject(ex);
                }
            });
            
            return resultPromise;
        }

        /// <summary>
        /// Returns a promise that resolves when all of the promises in the enumerable argument have resolved.
        /// Returns a promise of a collection of the resolved results.
        /// </summary>
        public static IPromise All(params IPromise[] promises)
        {
            return All((IEnumerable<IPromise>)promises); // Cast is required to force use of the other All function.
        }

        /// <summary>
        /// Returns a promise that resolves when all of the promises in the enumerable argument have resolved.
        /// Returns a promise of a collection of the resolved results.
        /// </summary>
        public static IPromise All(IEnumerable<IPromise> promises)
        {
            var promisesArray = promises.ToArray();
            if (promisesArray.Length == 0)
            {
                return Promise.Resolved();
            }

            var remainingCount = promisesArray.Length;
            var resultPromise = new Promise();

            promisesArray.Each((promise, index) =>
            {
                promise
                    .Catch(ex => {
                        if (resultPromise.CurState == PromiseState.Pending)
                        {
                            // If a promise errorred and the result promise is still pending, reject it.
                            resultPromise.Reject(ex);
                        }
                    })
                    .Done(() =>                     
                    {
                        --remainingCount;
                        if (remainingCount <= 0)
                        {
                            // This will never happen if any of the promises errorred.
                            resultPromise.Resolve();
                        }
                    });
            });

            return resultPromise;
        }

        /// <summary>
        /// Convert a simple value directly into a resolved promise.
        /// </summary>
        public static IPromise Resolved()
        {
            var promise = new Promise();
            promise.Resolve();
            return promise;
        }

        /// <summary>
        /// Convert an exception directly into a rejected promise.
        /// </summary>
        public static IPromise Rejected(Exception ex)
        {
            Argument.NotNull(() => ex);

            var promise = new Promise();
            promise.Reject(ex);
            return promise;
        }
    }
}