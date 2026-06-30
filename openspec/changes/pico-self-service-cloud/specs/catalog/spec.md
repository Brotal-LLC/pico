## ADDED Requirements

### Requirement: Service Catalog
The system SHALL provide a catalog of VM flavors (packages) that customers can browse without authentication.

#### Scenario: Browse catalog
- **WHEN** a visitor navigates to the catalog page
- **THEN** the system displays all active flavors sorted by price ascending
- **AND** each flavor shows: name, vCPUs, RAM, disk, price/hour, price/month

#### Scenario: Flavor detail
- **WHEN** a visitor selects a flavor
- **THEN** the system shows full details including category (e.g. "General Purpose", "Compute Optimized") and a description

### Requirement: OS Image List
The system SHALL provide a list of available OS images that can be selected when provisioning.

#### Scenario: List images
- **WHEN** a user views the provisioning form
- **THEN** the system displays all active images with name, OS, version, and size

### Requirement: Pricing Calculator
The system SHALL provide a real-time pricing estimate based on selected flavor and billing period.

#### Scenario: Hourly estimate
- **WHEN** a user selects a flavor
- **THEN** the system calculates the estimated hourly cost

#### Scenario: Monthly estimate
- **WHEN** a user selects a flavor and chooses monthly billing
- **THEN** the system calculates the estimated monthly cost with a discount applied (if any)