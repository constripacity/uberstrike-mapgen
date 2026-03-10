from braket.circuits import Circuit
from braket.devices import LocalSimulator
import boto3

# Test 1: Local simulator (free, runs on your Shadow PC)
device = LocalSimulator()
bell = Circuit().h(0).cnot(0, 1)
result = device.run(bell, shots=100).result()
print("Local simulator:", result.measurement_counts)

# Test 2: Verify AWS credentials
sts = boto3.client("sts")
identity = sts.get_caller_identity()
print(f"AWS Account: {identity['Account']}")
print(f"User ARN: {identity['Arn']}")
print("AWS credentials OK")
