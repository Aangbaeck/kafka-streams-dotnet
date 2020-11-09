﻿using Streamiz.Kafka.Net.Stream;
using Streamiz.Kafka.Net.Table.Internal;
using System;

namespace Streamiz.Kafka.Net.Processors
{
    internal class KTableKTableJoinProcessor<K, V1, V2, VR> : AbstractProcessor<K, Change<V1>>
    {
        private readonly IKTableValueGetter<K, V2> valueGetter;
        private readonly IValueJoiner<V1, V2, VR> joiner;
        private readonly bool sendOldValues;

        public KTableKTableJoinProcessor(IKTableValueGetter<K, V2> valueGetter, IValueJoiner<V1, V2, VR> joiner, bool sendOldValues)
        {
            this.valueGetter = valueGetter;
            this.joiner = joiner;
            this.sendOldValues = sendOldValues;
        }

        public override void Init(ProcessorContext context)
        {
            base.Init(context);
            valueGetter.Init(context);
        }

        public override void Process(K key, Change<V1> value)
        {
            if (key == null)
            {
                log.Warn($"{logPrefix}Skipping record due to null key. change=[{value}] topic=[{Context.Topic}] partition=[{Context.Partition}] offset=[{Context.Offset}]");
                return;
            }

            VR newValue = default;
            VR oldValue = default;
            var valueAndTsRight = valueGetter.Get(key);
            if (valueAndTsRight == null)
            {
                return;
            }

            long resultTs = Math.Max(Context.Timestamp, valueAndTsRight.Timestamp);

            if (value.NewValue != null)
            {
                newValue = joiner.Apply(value.NewValue, valueAndTsRight.Value);
            }

            if (sendOldValues && value.OldValue != null)
            {
                oldValue = joiner.Apply(value.OldValue, valueAndTsRight.Value);
            }

            Forward(key, new Change<VR>(oldValue, newValue), resultTs);
        }

        public override void Close()
        {
            base.Close();
            valueGetter.Close();
        }
    }
}
