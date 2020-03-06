﻿using kafka_stream_core.Processors;
using System.Collections.Generic;
using System.Linq;

namespace kafka_stream_core.Stream.Internal
{
    internal class ProcessorTopology
    {
        internal IList<string> SourceProcessorNames => new List<string>(SourceOperators.Keys);
        internal IList<string> SinkProcessorNames => new List<string>(SinkOperators.Keys);
        internal IList<string> ProcessorNames => new List<string>(ProcessorOperators.Keys);


        internal IProcessor Root { get; }
        internal IDictionary<string, IProcessor> SourceOperators { get; } = new Dictionary<string, IProcessor>();
        internal IDictionary<string, IProcessor> SinkOperators { get; } = new Dictionary<string, IProcessor>();
        internal IDictionary<string, IProcessor> ProcessorOperators { get; } = new Dictionary<string, IProcessor>();


        internal ProcessorTopology(
            IProcessor rootProcessor,
            IDictionary<string, IProcessor> sources,
            IDictionary<string, IProcessor> sinks,
            IDictionary<string, IProcessor> processors)
        {
            Root = rootProcessor;
            SourceOperators = sources;
            SinkOperators = sinks;
            ProcessorOperators = processors;
        }

        internal IProcessor GetSourceProcessor(string name)
        {
            var processor = SourceOperators.FirstOrDefault(kp => kp.Value is ISourceProcessor && (kp.Value as ISourceProcessor).TopicName.Equals(name));
            return processor.Value;
        }

        internal IEnumerable<string> GetSourceTopics() => SourceOperators.Select(o => (o.Value as ISourceProcessor).TopicName);
    }
}