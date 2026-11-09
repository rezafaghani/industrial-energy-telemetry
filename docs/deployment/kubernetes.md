# Kubernetes deployment

The Helm chart deploys the application workloads and expects reachable Kafka and PostgreSQL services. Stateful infrastructure is intentionally external to the application chart: production operators should use managed services or maintained operators with persistent volumes, backups, disruption budgets, and topology appropriate to their environment.

For kind, install PostgreSQL and Kafka first, install KEDA, load the IETP images into the cluster, then run:

```bash
helm upgrade --install ietp infrastructure/kubernetes/ietp \
  --set image.registry=ietp \
  --set image.tag=dev \
  --set opcUa.allowUnsecuredDevelopmentEndpoint=true
```

The unsecured override is only for an isolated local cluster. Production must mount provisioned application certificates and trust lists and leave it `false`.

Processors and inserters scale from Kafka consumer lag using KEDA. Their maximum is 12 because both source topics have 12 partitions; replicas beyond that remain idle. CPU may inform resource tuning, but it is not the primary backlog signal.

Compose includes single-node development Kafka/PostgreSQL. Kubernetes assumes durable, redundant infrastructure and external secret management. Replace the chart's placeholder Secret before installation.
