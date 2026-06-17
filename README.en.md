# Cisco Configurator

[Deutsch](README.de.md) | [English](README.en.md)

**Current version:** `0.15-pre.Release`  
[View changelog](CHANGELOG.md)

## Overview

Cisco Configurator is a C# WPF application for structured creation, management, validation, and documentation of Cisco IOS and IOS-XE configurations. It supports routers, Layer 3 switches, and Layer 2 switches and provides the required functions through clearly separated technical modules.

The application goes beyond basic configuration generation. It includes multi-device projects, IPAM, port planning, peer configuration generation, configuration analysis, SSH transfer, backups, intelligent network diagrams, ACL rule analysis, routing overlays, internal project versioning, a graphical Plugin Manager, SSH live inventory, exportable network plans, and complete project packages.


## Usage

Cisco Configurator is designed around a project-based workflow. Configuration, device management, validation, and network-plan functions use the same project state.

### 1. Create a project

1. Open the **Project** area.
2. Create a new network project or open an existing project.
3. Enter the project name, project number, organization, location, project manager, author, version, status, and description.
4. Save the project as a `.ciscoproject.json` file.

### 2. Select the target device

1. Select the device type in the top bar:
   - Router
   - Layer 3 switch
   - Layer 2 switch
2. Select the configuration mode:
   - without VRF
   - with VRF
3. Enter at least the hostname and required base settings in the **Base** area.

Visible modules and input fields are filtered according to the selected device type and mode.

### 3. Configure modules

1. Open the required technical area from the left navigation.
2. Enable the required modules.
3. Complete the input fields and review the available notes and tooltips.
4. Check dependencies between VLANs, interfaces, routing, ACLs, VRFs, and security functions.
5. Leave unused modules disabled because only active modules are included in the generated configuration.

### 4. Generate the configuration

1. Open the **Output** area.
2. Refresh the preview.
3. Review warnings, duplicate commands, and placeholders.
4. Copy the generated configuration or export it as a TXT file.
5. Enable `write memory` only when direct saving on the target device is intended.

### 5. Add the device to the project

1. Open the **Project** area.
2. Select **Capture current device**.
3. Configure and add additional routers or switches in the same way.
4. Existing project devices can be loaded, updated, duplicated, exported, or removed.

### 6. Use IPAM and port planning

1. Open the **IPAM** area.
2. Add networks manually or import them from the current or stored configuration.
3. Assign VLAN, prefix, gateway, DHCP range, device, and interface information.
4. Validate overlaps and duplicate gateway assignments.
5. Generate the interface and port plan from a current or stored device configuration.
6. Export IPAM and port-plan data as CSV when required.

### 7. Create connections and the diagram

1. Open the **Diagram** area.
2. Select the source device, source interface, target device, and target interface.
3. Select the connection type and enter an optional description.
4. Add the connection to the project.
5. Optionally define a site and topology role for each project device.
6. Use **Smart Layout** to arrange devices automatically by site and by WAN, Core, Distribution, and Access roles.
7. Import CDP or LLDP neighbor output to add missing connections between existing project devices automatically.
8. Enable the **Routing Overlay** to display OSPF areas, BGP AS numbers, EIGRP, IS-IS, VRFs, HSRP, and addressed routed links.
9. Continue adjusting devices through drag and drop.
10. Connections are routed orthogonally and avoid other device nodes where possible.
11. Drag the small waypoint on a connection to adjust its route manually.
12. Right-click the waypoint or connection label to restore automatic routing.
13. Use **Reset connection routes** to remove all manual waypoints.
14. Routers, Layer 3 switches, and Layer 2 switches use dedicated vector symbols.
15. Export the standalone network diagram as SVG.

Connections are created and maintained only in the **Diagram** area. The network plan automatically reuses these connection records.

### 8. Run analysis

The **Analysis** area provides functions including:

- dependency and conflict validation
- security audit
- configuration comparison
- rollback draft generation
- global search
- Cisco command analysis
- detection of unknown or unassigned commands

Review and resolve detected warnings and errors before export or transfer.

### Use the ACL editor and rule analysis

1. Open **Analysis** and select **ACL Editor**.
2. Import ACL rules from stored project devices or enter them manually in the table.
3. Edit sequence, action, protocol, source, destination, wildcards, and service values.
4. Add interface assignments with `IN` or `OUT` direction.
5. Run analysis for shadowed rules, redundant rules, duplicate sequences, broad `permit any any` rules, and unused ACLs.
6. Copy the generated ACL configuration block or export the rule set as CSV.

### 9. Import an existing configuration

1. Open the **Import** area.
2. Paste or load an existing Cisco configuration.
3. Start the analysis.
4. Review detected values and modules.
5. Manually assess unknown commands.
6. Verify imported data in the normal module views.

### 10. Use SSH and backups

1. Open the **SSH** area.
2. Enter the host, port, user, and authentication method.
3. Test the connection.
4. Transfer the current configuration only after validation.
5. Create a Running-Config or Startup-Config backup before making changes.
6. Backups can be exported, compared, and used as the basis for a rollback draft.

SSH passwords are not stored in project or AutoSave files.

#### SSH live inventory

The **Inventory** subtab uses the credentials configured in the SSH area and collects the current device state through Cisco `show` commands. It can collect:

- hostname, model, serial number, and IOS / IOS-XE version
- uptime and detected device type
- IPv4/IPv6 interfaces and descriptions
- VLANs, trunks, and port channels
- CDP/LLDP neighbors
- detected routing protocols, VRFs, and HSRP

Results are displayed as a preview first. The device, IPAM entries, and discovered neighbor links can then be imported selectively into the project or exported as JSON. Unsupported commands or insufficient permissions are recorded without aborting the complete inventory process.

### 11. Export the network plan

1. Complete the project information, devices, IPAM data, and connections.
2. Open the **Network plan** sub-area in **Diagram**.
3. Refresh the preview.
4. Export the network plan as HTML, DOCX, or PDF.

Depending on the available project data, the network plan includes:

- project information
- graphical topology
- device overview
- connections and interfaces
- IP networks and VLANs
- port information
- static routes
- ACL assignments
- VRFs
- routing protocols
- validation results and test commands

### Export a project package

Use **Project Package ZIP** to export the complete project state as one archive. The package contains:

- the complete `.ciscoproject.json` project file
- individual device configurations
- HTML network plan and SVG diagram
- IPAM, connection, ACL, route, and routing-protocol tables as CSV
- dependency, security, and ACL analysis results
- rollback drafts and previous configurations when device backups are available
- a machine-readable `manifest.json`

### 12. Save and continue the project

Save project changes regularly. AutoSave is disabled by default for new installations and can be enabled through Settings when required. Restoration and save-on-exit remain separately configurable. Project files do not contain SSH passwords, but they may contain sensitive network data and configurations and should be protected accordingly.

### 13. Manage project versions

Use **Project → Versions** to create, comment, compare, delete, and restore internal project snapshots. A version can be created automatically during normal project saving when configuration history is enabled in Settings. Before restoring an older version, the current state is saved automatically. Version history is stored inside the `.ciscoproject.json` file and is included in complete project-package exports.


## Supported device types

- Router
- Layer 3 switch
- Layer 2 switch
- configuration without VRF
- configuration with VRF

Modules and input fields are filtered according to device type and configuration mode.

## Main navigation

### Configuration

- Overview
- Base
- Management
- Interface / Ports
- Switching
- Routing
- Network Services
- Security/WAN

### Tools

- Subnetting
- Command Register
- SSH
- Import
- Peer
- Output

### Documentation

- Project
- IPAM
- Analysis
- Diagram

### System

- Settings
  - application settings
  - translation audit
  - Plugin Manager

## Configuration modules

### Base

- general device settings and hostname
- global device settings
- banners

### Management

- SSH and Line VTY
- local users and privilege levels
- AAA and console access
- RADIUS and TACACS+
- NTP
- Syslog
- SNMP
- logging

### Interface / Ports

- individual interfaces
- interface ranges
- subinterfaces
- Router-on-a-Stick
- interface profiles
- interface role model
- trunks and uplinks
- EtherChannel
- QinQ / 802.1ad
- basic QoS configuration

### Switching

- VLANs and SVIs
- VLAN / IP plan
- access switch configuration
- Voice VLAN
- Spanning Tree
- Port Security
- DHCP Snooping
- Dynamic ARP Inspection
- IP Source Guard
- Errdisable Recovery
- switch hardening

### Routing

- general routing settings
- static routes
- OSPF
- OSPFv3
- EIGRP
- IS-IS
- BGP
- HSRP
- VRRP
- GLBP
- VRF-Lite
- VRF-specific static routes
- VRF-specific OSPFv2 and OSPFv3
- VRF-specific BGP
- route maps
- prefix lists
- IP SLA and Object Tracking
- MPLS, LDP, and L3VPN

### Network Services

- IPv4 and IPv6 configuration
- DHCP
- IPv4 and IPv6 ACLs
- ACL assistant
- OSPFv3
- IPv6 routing protocols

### Security/WAN

- hardening
- NAT and PAT
- GRE
- GRE over IPsec
- site-to-site IPsec VPN
- Zone-Based Firewall
- DMZ assistant
- WAN failover
- custom commands

## Configuration generation

- module-based Cisco configuration generation
- live preview of active modules
- consolidation of active configuration areas
- detection of duplicate or conflicting commands
- text export from the Output area
- copying generated configuration
- serial transfer through COM / console
- optional `write memory`

The `TXT Export` and `Copy` functions are available only in the Output area and are no longer duplicated in the top header.

## Multi-device projects

Projects can contain multiple routers and switches. Stored data includes:

- project information
- devices and device types
- active modules
- input values
- generated configurations
- IPAM entries
- connections
- diagram positions
- backups

Additional project information:

- project name
- project number
- organization / customer
- location
- project manager
- author
- version
- status
- description

## IPAM and port planning

- centralized IPv4 and IPv6 network management
- VLAN assignment
- gateway documentation
- DHCP ranges
- device and interface assignment
- import from current and stored configurations
- network overlap detection
- duplicate gateway detection
- CSV export
- interface and port plan generated from Cisco configurations
- Access, Trunk, and Routed port display
- Voice VLAN, Native VLAN, and allowed VLANs
- Port-Channel and STP information

## Subnetting

The integrated subnet calculator supports IPv4 network planning and subnet division. Results can be reused for configuration and documentation.

## Command register and command analysis

- configuration commands with parameters and descriptions
- operational, show, test, and clear commands
- search by command and module
- analysis of individual Cisco command lines
- explanation of command components and parameter positions

## Import and analysis

Existing Cisco configurations can be imported and analyzed. The application detects known modules, fields, and configuration sections and lists commands that cannot be assigned separately.

Included analysis functions:

- dependency validation
- required-field validation
- conflict detection
- security audit
- global search
- configuration comparison
- rollback draft
- unknown-command validation

## Peer configuration

Requirements and configuration drafts for the remote peer can be derived from the current configuration. Peer configurations can then be added as separate project devices.

## SSH and backups

- connection test
- transfer of generated configurations
- OpenSSH with private key
- Plink with password
- configurable port and timing values
- Running-Config backup
- Startup-Config backup
- backup export
- comparison between a backup and a new configuration
- use of a backup as the basis for a rollback draft

Passwords are not stored in project or AutoSave files.

## Diagram and network plan

Connections are created and maintained in the Diagram area. Supported connection types include:

- Ethernet
- Access
- Trunk
- Port-Channel
- Routed Link
- WAN
- Tunnel
- Serial
- Fiber
- Wireless

Diagram functions:

- drag-and-drop positioning
- automatic layout
- orthogonal connection routing with obstacle detection
- draggable and persistent connection waypoints
- reset options for individual or all connection routes
- interface labels
- connection types
- custom connection descriptions
- different line colors and styles
- SVG export

Device types use dedicated vector symbols:

- router symbol with four directional axes
- Layer 3 switch symbol with horizontal and vertical forwarding
- Layer 2 switch symbol with Layer 2 port forwarding

The network plan uses the connections defined in the Diagram area and adds technical overviews for:

- project information
- devices
- connections
- IP networks
- VLANs
- interfaces and ports
- static routes
- ACL assignments
- VRFs
- routing protocols
- validation results
- test commands

Export formats:

- SVG for the network diagram
- HTML network plan with embedded vector topology
- DOCX network plan
- PDF network plan

## Plugin-capable module architecture

Additional configuration modules can be added through data-only `*.ciscoplugin.json` files. Plugins do not load executable assemblies; they contain localized module definitions and command templates only.

Supported plugin locations:

- `Plugins` next to the application
- `%APPDATA%/CiscoKonfigurator/Plugins`

Plugin modules are inserted into the existing technical areas. Module and field names must be unique. An example manifest and plugin documentation are included in the `Plugins` directory.

The **Plugin Manager** is available under **System → Settings**. It displays discovered plugins, version, state, module count, languages, and validation diagnostics. Plugins can be enabled or disabled there. Module-interface changes take effect after an application restart. Validation covers localization completeness, duplicate IDs, module and field conflicts, generator assignments, required fields, and unknown placeholders.

## Localization

- German user interface
- English user interface
- embedded JSON resources
- localized modules
- localized commands
- localized tooltips
- fallback for missing translations
- language selection through Settings

## Operation and data protection

- save and load templates
- AutoSave disabled by default and optionally configurable
- restoration of the latest project state
- configurable start page
- search and filtering functions
- detailed multi-line tooltips
- Single-File-ready project structure
- SSH passwords are not stored

## Project files

Network projects are stored as `.ciscoproject.json` files. The file contains the project structure, devices, connections, IPAM data, diagram positions, manual connection waypoints, inventory data, internal project versions, and technical metadata.

## Technical foundation

- C#
- .NET 8
- WPF
- embedded JSON catalogs
- data-driven module plugins
- System.IO.Ports
- native WPF vector graphics
- HTML, SVG, DOCX, and PDF output

## Disclaimer

Cisco Configurator is an assistance tool for planning, generating, analyzing, and documenting Cisco configurations. It does not replace professional review by qualified network administrators.

- Generated configurations must be reviewed completely before deployment.
- Commands may differ or be unsupported depending on the platform, device model, license, IOS / IOS-XE version, and enabled feature set.
- Configurations should first be validated in a lab, test, or staging environment.
- Current backups, a rollback plan, and applicable change-management procedures must be in place before production changes.
- Automated analysis cannot identify every misconfiguration, security risk, dependency, or operational impact.
- Credentials, keys, project files, backups, and exported configurations must be protected appropriately by the user.
- The user is responsible for compliance with internal policies, data-protection requirements, security requirements, licensing terms, and applicable laws.
- Use of the software and its generated results is at the user's own risk. No warranty or liability is accepted for outages, misconfigurations, data loss, security incidents, or other damages to the extent permitted by law.

Cisco, Cisco IOS, Cisco IOS XE, and other product names are trademarks or registered trademarks of Cisco Systems, Inc. This project is not affiliated with, endorsed by, or supported by Cisco Systems, Inc. and is not an official Cisco product.
