using System;
using System.Collections.Generic;
using System.Linq;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Streamiz.Kafka.Net.Crosscutting;
using Streamiz.Kafka.Net.Errors;
using Streamiz.Kafka.Net.Kafka;
using Streamiz.Kafka.Net.Kafka.Internal;
using Streamiz.Kafka.Net.Metrics;
using Streamiz.Kafka.Net.Metrics.Internal;
using Streamiz.Kafka.Net.Processors.Internal;
using Streamiz.Kafka.Net.Stream;

namespace Streamiz.Kafka.Net.Processors
{
    internal class ExternalProcessorTopology
    {
        private static readonly ILogger log = Logger.GetLogger(typeof(ExternalProcessorTopology));

        internal enum ExternalProcessorTopologyState
        {
            RUNNING,
            BUFFER_FULL,
            PAUSED,
            RESUMED
        }

        internal class ExternalStreamTask : AbstractTask
        {
            public override TaskId Id { get; }
            private ExternalStreamTask(TaskId id) {
                Id = id;
            }
            public static ExternalStreamTask Create(TaskId id) => new ExternalStreamTask(id);
            
            #region Not implemented
            
            public override IDictionary<TopicPartition, long> PurgeOffsets { get; }
            public override PartitionGrouper Grouper { get; }
            public override bool CanProcess(long now)
            {
                throw new NotImplementedException();
            }

            public override void Suspend()
            {
                throw new NotImplementedException();
            }

            public override void MayWriteCheckpoint(bool force = false)
            {
                throw new NotImplementedException();
            }

            public override void Close()
            {
                throw new NotImplementedException();
            }

            public override IStateStore GetStore(string name)
            {
                throw new NotImplementedException();
            }

            public override void InitializeTopology()
            {
                throw new NotImplementedException();
            }

            public override void RestorationIfNeeded()
            {
                throw new NotImplementedException();
            }

            public override bool InitializeStateStores()
            {
                throw new NotImplementedException();
            }

            public override void Resume()
            {
                throw new NotImplementedException();
            }

            public override void Commit()
            {
                throw new NotImplementedException();
            }
            #endregion
        }
        
        private ISourceProcessor Processor { get; }
        private Queue<ConsumeResult<byte[], byte[]>> BufferedRecords { get; } = new();
        private RetryPolicy RetryPolicy { get; }
        public ExternalProcessorTopologyState State { get; internal set; }
        public List<TopicPartition> PreviousAssignment { get; set; }
        public int BufferSize => BufferedRecords.Count;

        private readonly IRecordCollector recordCollector;
        private ProcessorContext context;
        private readonly string logPrefix;
        private readonly Dictionary<TopicPartition, long> consumedOffsets = new(); 

        public ExternalProcessorTopology(
            TaskId taskId,
            ISourceProcessor sourceProcessor, 
            IProducer<byte[], byte[]> producer,
            IStreamConfig config,
            StreamMetricsRegistry streamMetricsRegistry)
        {
            State = ExternalProcessorTopologyState.RUNNING;
            Processor = sourceProcessor;

            logPrefix = $"external-task[{taskId.Id}] ";
            
            recordCollector = new RecordCollector(logPrefix, config, taskId, NoRunnableSensor.Empty);
            recordCollector.Init(ref producer);
            
            context = new ProcessorContext(ExternalStreamTask.Create(taskId), config, null, streamMetricsRegistry);
            context.UseRecordCollector(recordCollector);
            
            Processor.Init(context);
            
            var asyncProcessor = Processor
                .Next
                .SelectMany(t => t.Next)
                .OfType<IAsyncProcessor>()
                .FirstOrDefault();
            
            RetryPolicy = asyncProcessor != null ? asyncProcessor.Policy : RetryPolicyBuilder.NewBuilder().Build();
            if(RetryPolicy.TimeoutMs <= 0)
                RetryPolicy.TimeoutMs = (long)config.MaxPollIntervalMs / 3;
            
            if(0.8 * config.MaxPollIntervalMs <= RetryPolicy.RetryBackOffMs * RetryPolicy.NumberOfRetry)
                log.LogWarning("Be carefully about your retry policy => retry.backoff.ms * number.of.retry is near or greater than consumer configuration MaxPollIntervalMs. It's can be risky and you can process multiple times the same message");
            
            if(0.8 * config.MaxPollIntervalMs <= RetryPolicy.TimeoutMs)
                log.LogWarning("Be carefully about your retry policy => retry.timeout.ms is near or greater than consumer configuration MaxPollIntervalMs. It's can be risky and you can process multiple times the same message");
        }

        public void Process(ConsumeResult<byte[], byte[]> record)
        {
            bool readingBuffer = false, doNotDequeue = false;
            ConsumeResult<byte[], byte[]> recordToProcess = null;
            try
            {
                if (BufferedRecords.Any())
                {
                    recordToProcess = BufferedRecords.Peek();
                    log.LogDebug($"{logPrefix}Take one record into the buffer for process it (buffer size : {BufferSize})");
                    readingBuffer = true;
                }
                else
                    recordToProcess = record;
                
                if (recordToProcess != null)
                {
                    context.SetRecordMetaData(recordToProcess);
                    long latency = ActionHelper.MeasureLatency(() => Processor.Process(recordToProcess));
                    log.LogDebug($"{logPrefix}Process record with this following metadata ({recordToProcess.TopicPartitionOffset}) in {latency} ms");
                    consumedOffsets.AddOrUpdate(recordToProcess.TopicPartition, recordToProcess.Offset.Value);
                }
            }
            catch (Exception e) when (e is NoneRetryableException or NotEnoughtTimeException)
            {
                if (RetryPolicy.EndRetryBehavior == EndRetryBehavior.BUFFERED)
                {
                    log.LogInformation($"{logPrefix}Record with this following metadata ({recordToProcess.TopicPartitionOffset}) exceed the retry policy. Add this record into the local buffer and will be reprocessed later.");
                    if (!readingBuffer)
                        BufferedRecords.Enqueue(record);
                    else
                        doNotDequeue = true;

                    if (BufferedRecords.Count >= RetryPolicy.MemoryBufferSize)
                    {
                        State = ExternalProcessorTopologyState.BUFFER_FULL;
                        log.LogWarning($"{logPrefix}The local buffer is FULL. Partitions's topic {recordToProcess.Topic} will be paused");
                    }
                }
                else if (RetryPolicy.EndRetryBehavior == EndRetryBehavior.SKIP)
                {
                    log.LogInformation($"{logPrefix}Record with this following metadata ({recordToProcess.TopicPartitionOffset}) exceed the retry policy. The retry policy behavior will be skipped this message and process next one");
                    consumedOffsets.AddOrUpdate(recordToProcess.TopicPartition, recordToProcess.Offset.Value);
                }
                else if (RetryPolicy.EndRetryBehavior == EndRetryBehavior.FAIL)
                {
                    log.LogInformation(
                        $"{logPrefix}Record with this following metadata ({recordToProcess.TopicPartitionOffset}) exceed the retry policy. The retry policy behavior is defined as a failure.");
                    throw;
                }
            }
            finally
            {
                if(readingBuffer && !doNotDequeue)
                    BufferedRecords.Dequeue();
                
                if(readingBuffer && record != null) 
                    BufferedRecords.Enqueue(record);
            }
        }

        public void Flush()
        {
            log.LogDebug($"{logPrefix}Flushing producer happens");
            recordCollector.Flush();
        }

        public void ClearBuffer()
        {
            log.LogDebug($"{logPrefix}Clearing local buffer and restore the state to RUNNING (previous state : {State})");
            BufferedRecords.Clear();
            State = ExternalProcessorTopologyState.RUNNING;
        }
        
        public void ClearOffsets()
        {
            consumedOffsets.Clear();
        }
        
        public IEnumerable<TopicPartitionOffset> GetCommitOffsets()
        {
            foreach (var kp in consumedOffsets)
            {
                yield return new TopicPartitionOffset(kp.Key, kp.Value + 1);
            }
        }
    }
}