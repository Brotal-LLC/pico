## ADDED Requirements

### Requirement: Customer Registration
The system SHALL allow a visitor to register as a customer by providing email, name, and password.

#### Scenario: Successful registration
- **WHEN** a visitor submits valid email, name, and password
- **THEN** a customer account is created with role `Customer`
- **AND** a session cookie is set
- **AND** the user is redirected to the catalog page

#### Scenario: Duplicate email
- **WHEN** a visitor registers with an email that already exists
- **THEN** the system returns a 409 Conflict error with a clear message

### Requirement: Customer Login
The system SHALL authenticate a customer via email + password and set a cookie-based session.

#### Scenario: Successful login
- **WHEN** a user submits valid credentials
- **THEN** a session cookie is set with the user's ID and role
- **AND** the user is redirected to the dashboard

#### Scenario: Invalid credentials
- **WHEN** a user submits incorrect email or password
- **THEN** the system returns a 401 Unauthorized error

### Requirement: Role-Based Access
The system SHALL enforce two roles: `Customer` and `Admin`, with role-based authorization on all API endpoints.

#### Scenario: Customer accesses own resources
- **WHEN** a customer requests `/api/resources`
- **THEN** only resources owned by that customer are returned

#### Scenario: Customer cannot access admin endpoints
- **WHEN** a customer requests `/api/admin/*`
- **THEN** the system returns 403 Forbidden

#### Scenario: Admin accesses all resources
- **WHEN** an admin requests `/api/admin/resources`
- **THEN** all resources across all users are returned