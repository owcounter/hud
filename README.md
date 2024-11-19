# OWCOUNTER HUD

OWCOUNTER HUD is a lightweight, non-intrusive heads-up display application that provides hero recommendations and team composition analysis for Overwatch 2. The HUD appears on top of your game, offering insights without the need to alt-tab or check another screen.

## Features

- 🎯 Hero recommendations based on scoreboard screenshots
- 📊 Team composition analysis
- 🔄 Updates when you take a new scoreboard screenshot
- 🎮 Non-intrusive HUD that doesn't interfere with gameplay
- 🔒 Zero game memory interaction - uses only scoreboard screenshots
- 🎨 Semi-transparent interface
- ⌨️ Quick toggle with F2 hotkey
- ⚙️ Customizable recommendations through the meta page

## System Requirements

- Windows 10/11 (64-bit)
- .NET Core 8.0 or later
- Active internet connection
- Overwatch 2 (Battle.net or Steam version)
- Game must run in Windowed Fullscreen mode
- Supported resolutions: 2560x1440 or 1920x1080

## Installation

1. Download the latest release from [GitHub Releases](https://github.com/owcounter/hud/releases)
2. Extract the ZIP file to your preferred location
3. Run `OwcounterHUD.exe`
4. Log in with your OWCOUNTER account

## Quick Start Guide

1. Launch OWCOUNTER HUD and sign in
2. Start Overwatch 2 in Windowed Fullscreen mode
3. The HUD will automatically position itself over your game
4. Press F2 to toggle the HUD visibility
5. Visit the [Meta page](https://owcounter.com/meta) to customize hero relationships and preferences:
   - Define counter relationships between heroes
   - Set hero synergies for optimal team compositions
   - Configure map-specific strengths and weaknesses
   - Customize composition preferences (Dive/Poke/Brawl)
6. In Overwatch 2, configure your screenshot hotkey:
   - Go to Options > Controls > Interface > Miscellaneous
   - Note your screenshot hotkey or set up an additional one
7. Ensure you're using default game colors:
   - Go to Options > Controls > Color Blind Options
   - Set Enemy UI Color to "Red"
   - Set Team UI Color to "Blue"
8. To analyze team compositions in a game:
   - Hold Tab to display the scoreboard
   - While holding Tab, use your screenshot hotkey to capture the scoreboard
   - View recommendations on the HUD
   - Repeat this process whenever you want to analyze updated team compositions

## Controls

- **F2**: Toggle HUD visibility
- **Tab + Screenshot Hotkey**: Capture scoreboard for analysis
- HUD automatically hides when:
  - Overwatch 2 is not running
  - Game is minimized
  - You switch to another window

## Configuration

### Setting Up Screenshots

1. In Overwatch 2:
   - Go to Options > Controls > Interface > Miscellaneous
   - Configure your screenshot hotkey
   - Recommended: Bind to an easily accessible key (e.g., Home, Page Up/Down)
   - Screenshots must be taken while holding Tab to show the scoreboard

### Game Display Settings

1. In Overwatch 2:
   - Set display mode to "Windowed Fullscreen"
   - This is required for proper HUD functionality

### Customizing Recommendations

1. Visit [owcounter.com/meta](https://owcounter.com/meta)
2. Sign in with your OWCOUNTER account
3. Customize your meta preferences:
   - Adjust hero counter relationships
   - Define hero synergies
   - Set map-specific preferences
   - Configure composition type affinities
4. Your customized settings will be automatically applied to the HUD

## Troubleshooting

### HUD Not Visible
- Verify Overwatch 2 is running in Windowed Fullscreen mode
- Press F2 to toggle visibility
- Check if Overwatch 2 is running
- Restart the HUD application

### Screenshot Issues
- Make sure you're using default UI colors (Enemy: Red, Team: Blue) in Color Blind Options
- The default Print Screen key may activate cursor selection mode
  - Press Escape to continue playing
  - Consider using a different screenshot hotkey
- If custom hotkey stops working:
  - Use Print Screen as temporary workaround
  - Restart Overwatch 2 to restore functionality
- If analysis isn't working:
  - Verify you can see the full scoreboard in your screenshots
  - Check that no UI elements are blocking the scoreboard

### Authentication Issues
- Verify your OWCOUNTER credentials
- Check your internet connection
- Try logging out and back in
- Clear cache by deleting `owcounter_oauth_token.json`

### Recommendation Issues
- Ensure you're logged in on both the HUD and website
- Check your meta customizations on owcounter.com/meta
- Try refreshing your meta settings page
- Log out and back in to sync your preferences

## Privacy & Security

- 🔒 No game memory access or injection
- 📸 Scoreboard screenshots are processed but not stored
- 🔐 Secure authentication through Keycloak
- 🔍 Transparent, open-source code

## Support

- Join our [Discord server](https://discord.gg/nDA9CAkwbQ) for community support
- Visit [OWCOUNTER](https://owcounter.com) for more information
- Report bugs through [GitHub Issues](https://github.com/owcounter/hud/issues)

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details
