
# Tracker powered by a discord bot

This project is a bot managable trough discord, which tracks certain actions of a selected user and reports them in a customizable channel.

Screenshot of testing the bot on first startup(subject to change):
![{AA895430-262C-49E8-8607-35F9332D8E44}](https://github.com/user-attachments/assets/6a944ee6-6339-4d6f-85f8-35196d637ab6)


# PROJECT IS IN EARLY DEV
## How-to
### Requirements
- dotNET Runtime (tested with 8) (future releases might be standalone)

### [Video tutorial link (unaviable for now sry)](https://youtu.be/wgk_KMtgaXk)


- Create a USER alt account and join as many servers as you know your target is in (yes you have to do that for discord message tracking)
- start the program (SpyAndScrape.exe)
- on first startup the user has to input a bot token, of a bot in a (preferably private) server. Create one here: https://discord.com/developers/applications
- After you should input the channel id the bot has access to (use admin perms if you dont want hassle)
- Once all data is entered and you see 
    "[time] Gateway     Connected",
    "Bot is connected" (in the console_output.txt)
    
    -> You shoud recive a starting message in the channel youve entered the id for
- use /startinfo for a little more context, and /configchange to change the values for you
    
    -> e.g. Track Discord Activity to true (to activate tracking) or false (to disable)

- after configuring use /restart to apply changes (some require restart)

- thats it.


## Config

Config is probably the main part of the program, used to direct it.
Following options are aviable:


| **Name**                     | **Description**                                                                 | **Codename (in JSON)**                     | **Var Type**          |
|------------------------------|---------------------------------------------------------------------------------|--------------------------------------------|-----------------------|
| **Target Name**               | Defines the name of the target (purely decorative)                              | `generalTargetName`                        | `string`              |
| **Bot Decoration**            | Manages the default bot decoration (silly cat by default)                       | `generalBotDecoration`                     | `string`              |
| **Bot Timeout**               | Specifies the time limit the bot is resting (in seconds)                        | `generalBotTimeout`                        | `int`                 |
| **Startup Message**           | Determines if the bot sends a starting message upon startup                     | `sendStartingMessageOnStartup`             | `int` (0 or 1, boolean)|
| **Bot Setup Channel ID**     | Channel ID used for bot setup operations (in app required)                       | `generalBotSetupChannelId`                 | `ulong`               |
| **Bot Log Channel ID**       | Channel ID where bot logs are sent                                              | `generalBotLogChannelId`                   | `ulong`               |
| **Important Channel ID**     | Channel ID for sending important notifications                                 | `generalBotImportantChannelId`             | `ulong`               |
| **Who to Ping**               | Defines who the bot should ping for notifications                              | `generalWhoToPing`                         | `string`              |
| **Track Discord Activity**   | True or false tracking of Discord messages (more soon) (required to track)      | `trackDiscord`                             | `bool`                |
| **Discord Tracking Id**     | Id used for tracking Discord activity (required to track)                        | `discordTrackingId`                       | `ulong`              |
| **Discord Tracking Token**   | Token for authenticating Discord tracking (user account) (required to track)   | `discordTrackingToken`                     | `string`              |
| **Discord Log Level**        | Defines the log level for Discord tracking (also determines if ping) (1-3)      | `discordTrackingLogLevel`                  | `int`                 |
| **Track Roblox Activity**    | Enables or disables tracking of Roblox activity (required to track)              | `trackRoblox`                              | `bool`                |
| **Roblox Tracking User ID**  | User ID used for tracking Roblox activity (required to track)                   | `robloxTrackingUserId`                     | `ulong`               |

- **`string`** just text
- **`int`** is used for whole numbers, including identifiers like log levels
- **`bool`** is used for true/false or binary values (like timeout)
- **`ulong`** is used for large numbers, especially for channel and user IDs

#### **Additionals**:
- required means you need to change this if you want to track that 
 - logLevel can only be between 1-3 (including)
 - for "Startup Message" its an 0 or 1, not bool, bc I had some parsing problems



## TODO (future plans)

- Add elevated notifications for deletion of messages
- (more services ?)
- try to apply changes without having to /restart
- more commands for control



## INFO

> WARNING: AUTOMATING NON BOT ACCOUNTS IS AGAINST THEIR TOS, I DO NOT TAKE RESPONSIBILITY OF ANY PROBLEMS CAUSED
 - [Discords tos reffering to context](https://support.discord.com/hc/en-us/articles/115002192352-Automated-User-Accounts-Self-Bots)

> Users must adhere to all applicable laws and regulations when using the Software. The Software must not be used for any illegal activities, including but not limited to stalking or harassment.
