﻿using System;
using System.Collections.Generic;
using System.Threading;
using Raven.Client.Util.RateLimiting;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents
{
    public class ExecuteRateLimitedOperations<T> : TransactionOperationsMerger.MergedTransactionCommand
    {
        private readonly Queue<T> _documentIds;
        private readonly Func<T, TransactionOperationsMerger.MergedTransactionCommand> _commandToExecute;
        private readonly RateGate _rateGate;
        private readonly OperationCancelToken _token;
        private readonly int? _batchSize;
        private readonly CancellationToken _cancellationToken;

        internal ExecuteRateLimitedOperations(
            Queue<T> documentIds, 
            Func<T, TransactionOperationsMerger.MergedTransactionCommand> commandToExecute, 
            RateGate rateGate,
            OperationCancelToken token, 
            int? batchSize)
        {
            _documentIds = documentIds;
            _commandToExecute = commandToExecute;
            _rateGate = rateGate;
            _token = token;
            _batchSize = batchSize;
            _cancellationToken = token.Token;
        }

        public bool NeedWait { get; private set; }

        public long Processed { get; private set; }

        public override long Execute(DocumentsOperationContext context, TransactionOperationsMerger.RecordingState recording)
        {
            var count = 0;
            foreach (T id in _documentIds)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                _token.Delay();

                if (_rateGate != null && _rateGate.WaitToProceed(0) == false)
                {
                    NeedWait = true;
                    break;
                }

                count++;
                var command = _commandToExecute(id);
                try
                {
                    Processed += command?.Execute(context, recording) ?? 0;
                }
                finally
                {
                    if (command is IDisposable d)
                        d.Dispose();
                }

                if (_batchSize != null && Processed >= _batchSize)
                    break;

                if (context.CanContinueTransaction == false)
                    break;

                if (context.CachedProperties.NeedClearPropertiesCache())
                {
                    context.CachedProperties.ClearRenew();
                }
            }

            var tx = context.Transaction.InnerTransaction.LowLevelTransaction;
            tx.OnDispose += _ =>
            {
                if (tx.Committed == false)
                    return;

                for (int i = 0; i < count; i++)
                {
                    _documentIds.Dequeue();
                }
            };
            
            return Processed;
        }

        public override TransactionOperationsMerger.IReplayableCommandDto<TransactionOperationsMerger.MergedTransactionCommand> ToDto<TTransaction>(TransactionOperationContext<TTransaction> context)
        {
            throw new NotSupportedException($"ToDto() of {nameof(ExecuteRateLimitedOperations<T>)} Should not be called");
        }

        protected override long ExecuteCmd(DocumentsOperationContext context)
        {
            throw new NotSupportedException("Should only call Execute() here");
        }
    }
}
