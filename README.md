# ASCOM Telescope Proxy Hub

## Introduction
This is a 
* ASCOM Telescope Server which connects to another ASCOM Telescope driver.

The purpose of this server is to make obsolete drivers working again. Made especially for ASA DDM legacy mounts.

## Features
* This driver implementats a virtual ASCOM Telescope V3 driver and passes the calls to the original driver.
* Properties which are missing in the original driver are simulated in virtual driver.
* Added validation of property values
* Added MoveAxis and AxisRates
* Added Slew settle time
* The driver is able to connect to the original driver via COM or TCP/IP (Alpaca)
  

The standard use case is to connect to the ASCOM Telescope Proxy Hub with a program which is not able to connect to the original ASCOM Telescope driver.

```mermaid
flowchart LR
    A[Program A] --> B(ASCOM Proxy Hub)
    B --> C(ASCOM Telescope)
    C -->|Physical connection| D[Mount]
    subgraph ASCOM
       B
       C
    end
```

You can also connect multi programs to the Proxy Hub and to the orignal driver at the same time!

```mermaid
flowchart LR
    A[Program A] --> B(ASCOM Proxy Hub)
    A1[Program B] --> B(ASCOM Proxy Hub)
    B --> C(ASCOM Telescope)
    A2[Program C] --> C(ASCOM Telescope)
    A3[Program D] --> C(ASCOM Telescope)
    C -->|Physical connection| D[Mount]
    subgraph ASCOM
       B
       C
    end
```



## Installation

Download the latest release from [here](https://github.com/photon1503/ASCOMProxyHub/releases/latest) and run the installer.
If Windows SmartScreen window appears, please click on "more info" and "run anyway"

<img width="399" alt="image" src="https://github.com/photon1503/ASCOMProxyHub/assets/14548927/6502b511-58cc-46b7-9265-4c402780e712">

## Usage

1. In your client application, select "ASCOM Telescope Driver for phonotProxyHub" as telescope driver.
   
   <img width="378" alt="image" src="https://github.com/photon1503/ASCOMProxyHub/assets/14548927/d7576e36-31ca-4152-b2cc-1bff31d97b73">

3. Configure the necessary settings (one time only)
   
   <img width="272" alt="image" src="https://github.com/photon1503/ASCOMProxyHub/assets/14548927/c0b9df00-ab6e-4fac-84bc-759371f86e6b">
   
   - Choose the original ASCOM Driver
   - Set Maximum slew speed for MoveAxis() commands
   - Set Slew settle time, or 0 to disable it
  
4. Connect! and you are good
   
   <img width="667" alt="image" src="https://github.com/photon1503/ASCOMProxyHub/assets/14548927/e775168c-c77e-4bad-9978-9ce9d8b64fc2">
