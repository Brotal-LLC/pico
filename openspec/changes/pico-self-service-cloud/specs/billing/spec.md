## ADDED Requirements

### Requirement: Invoice Generation
The system SHALL generate monthly invoices for each customer based on their resource usage.

#### Scenario: Invoice creation
- **WHEN** a new billing period starts (first of each month)
- **THEN** the system generates an invoice for each customer with active resources
- **AND** each invoice line item includes: resource name, flavor, hours used, rate, and total amount

#### Scenario: No active resources
- **WHEN** a customer has no active resources in the billing period
- **THEN** no invoice is generated for that customer

### Requirement: Invoice Payment
The system SHALL allow a customer to mark an invoice as paid (simulated payment).

#### Scenario: Pay invoice
- **WHEN** a customer clicks "Pay Now" on a pending invoice
- **THEN** the invoice status transitions from `Pending` to `Paid`
- **AND** an audit log entry is created

#### Scenario: Already paid
- **WHEN** a customer attempts to pay an already-paid invoice
- **THEN** the system returns 409 Conflict

### Requirement: Invoice Listing
The system SHALL display a list of all invoices for the current customer with status and total.

#### Scenario: View invoices
- **WHEN** a customer navigates to the billing page
- **THEN** the system displays all invoices sorted by date descending
- **AND** each invoice shows: period, total amount, status badge, and a "Pay" button if pending

#### Scenario: Empty invoices
- **WHEN** a customer has no invoices
- **THEN** the system displays a helpful empty state with a CTA to provision a resource

### Requirement: Admin Metrics
The system SHALL provide admin-level summary metrics for operational overview.

#### Scenario: Admin views metrics
- **WHEN** an admin navigates to the admin dashboard
- **THEN** the system displays: total users, total active resources, total revenue (paid invoices), pending invoice count, and resource status breakdown