﻿- Schema avro used is avro.avsc
- Bean Class was generated with [AvroGen tools](https://www.nuget.org/packages/Confluent.Apache.Avro.AvroGen/)
- Schema was pushed into schema registry with subject name (person-value)
- Now you can start sample-stream-registry project. This topoloy read data into topic where value is serialized in Avro and key is juste a string, filter + map + to processor
