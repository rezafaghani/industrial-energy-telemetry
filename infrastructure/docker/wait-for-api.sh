#!/bin/sh
set -eu

echo "Waiting for the telemetry API to be ready"
until wget -q -T 5 -O /dev/null http://api:8080/health/ready; do
  sleep 2
done
