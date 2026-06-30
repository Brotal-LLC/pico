## ADDED Requirements

### Requirement: Resource Provisioning
The system SHALL allow a customer to provision a new resource by selecting a flavor, image, and providing a name.

#### Scenario: Successful provisioning
- **WHEN** a customer submits a valid provisioning request (flavor_id, image_id, name)
- **THEN** a new Resource is created with status `Created`
- **AND** the provisioning backend is invoked asynchronously
- **AND** the resource transitions to `Provisioning` then `Running`

#### Scenario: Invalid flavor
- **WHEN** a customer submits a provisioning request with an inactive or non-existent flavor_id
- **THEN** the system returns 400 Bad Request with a clear error message

#### Scenario: Name collision
- **WHEN** a customer submits a provisioning request with a name that's already in use by one of their active resources
- **THEN** the system returns 409 Conflict

### Requirement: Provisioning State Machine
The system SHALL enforce a state machine for resource lifecycle with the following valid transitions:

```
Created → Provisioning → Running ⇄ Stopped → Terminated
                ↓
            Failed
```

#### Scenario: Invalid transition
- **WHEN** a customer attempts to start a resource that is `Terminated`
- **THEN** the system returns 409 Conflict with message "Cannot start a terminated resource"

#### Scenario: Valid stop
- **WHEN** a customer stops a `Running` resource
- **THEN** the resource transitions to `Stopped` via the provisioning backend
- **AND** a ResourceEvent is logged

### Requirement: Resource Usage Metering
The system SHALL collect and display usage data for each active resource.

#### Scenario: View usage
- **WHEN** a customer views a resource detail page
- **THEN** the system displays current CPU usage, RAM usage, disk usage, and network I/O
- **AND** in `mock` mode, usage data is simulated with realistic random values
- **AND** in `docker` mode, usage data is read from `docker stats`
- **AND** in `openstack` mode, usage data is read from Nova API

### Requirement: Resource Events Stream
The system SHALL provide real-time status updates via Server-Sent Events.

#### Scenario: SSE connection
- **WHEN** a customer opens a resource detail page
- **THEN** the frontend opens an SSE connection to `/api/resources/{id}/events`
- **AND** receives status transitions as they happen

#### Scenario: SSE fallback
- **WHEN** SSE is not supported or connection fails
- **THEN** the frontend falls back to polling `/api/resources/{id}` every 2 seconds

### Requirement: Provisioning Backend Abstraction
The system SHALL support three provisioning backend modes selectable via `PROVISIONING_MODE` environment variable.

#### Scenario: Mock mode (default)
- **WHEN** `PROVISIONING_MODE` is unset or set to `mock`
- **THEN** provisioning operations simulate state transitions in the database with a 2-5 second delay
- **AND** no external services are required

#### Scenario: Docker mode
- **WHEN** `PROVISIONING_MODE` is set to `docker`
- **THEN** provisioning operations create/stop/start/terminate real Docker containers
- **AND** resource CPU/RAM limits match the selected flavor
- **AND** the container's IP is stored as the resource's `ip_address`

#### Scenario: OpenStack mode
- **WHEN** `PROVISIONING_MODE` is set to `openstack`
- **THEN** provisioning operations call Nova API to create/stop/start/terminate real VMs
- **AND** the VM's floating IP is stored as the resource's `ip_address`