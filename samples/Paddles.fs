// Example: a paddle-and-ball game for the Commodore 64, in F#. You are the
// left paddle, with W and S or a joystick in port 2, against the computer on
// the right; the first to 7 points wins. Wait at the title screen and the
// computer plays itself. M turns the music on and off; Q goes back to the
// title, and from there to BASIC.
// Build: in a .fsproj of the Zero C# SDK (as sdk/test/fixtures/fspaddles),
//        dotnet build -p:ZeroTarget=c64
//
// The same game as samples/Paddles.cs, written as F#: records hold the
// game's state, struct unions say who moves a paddle and what each frame of
// play led to, and active patterns say where the ball is.
//
// Zero mode never frees memory, so nothing in a frame of play may allocate:
// the unions are [<Struct>], and the active patterns return a voption
// ([<return: Struct>]) instead of an option, which would be an object.
//
// Typing T at the title screen plays one match, computer against computer,
// and reports on it. The match is the same on every run, and the same as the
// C# game's. Exit code = number of failed checks.
module Paddles

open System

[<Struct>]
type Side =
    | Left
    | Right

    member side.Opposite =
        match side with
        | Left -> Right
        | Right -> Left

// The playfield, in sprite coordinates: the screen's 320x200 pixels start at
// x 24 and y 50.
[<RequireQualifiedAccess>]
module Court =
    [<Literal>]
    let Left = 24
    [<Literal>]
    let Right = 344
    [<Literal>]
    let Top = 50
    [<Literal>]
    let Bottom = 250
    [<Literal>]
    let Middle = (Left + Right) / 2
    [<Literal>]
    let Center = (Top + Bottom) / 2
    [<Literal>]
    let LeftFace = 40 // where the paddles face the ball
    [<Literal>]
    let RightFace = 328
    [<Literal>]
    let PaddleHeight = 42 // a sprite's 21 rows, doubled
    [<Literal>]
    let BallSize = 4
    [<Literal>]
    let WinningScore = 7

    let clamp paddleTop = Math.Clamp(paddleTop, Top, Bottom - PaddleHeight)
    let clampBall ballTop = Math.Clamp(ballTop, Top, Bottom - BallSize)

// The ball: its top left corner, and how far it moves each frame.
[<Struct>]
type Ball =
    { X: int
      Y: int
      Dx: int
      Dy: int }

    member ball.Middle = ball.Y + Court.BallSize / 2

    member ball.Meets paddleTop =
        ball.Y + Court.BallSize > paddleTop && ball.Y < paddleTop + Court.PaddleHeight

[<Struct>]
type Score =
    { Left: int
      Right: int }

    member score.Winner =
        if score.Left = Court.WinningScore then ValueSome Side.Left
        elif score.Right = Court.WinningScore then ValueSome Side.Right
        else ValueNone

// docs:unions
// Who moves a paddle, and how fast.
[<Struct>]
type Controller =
    | Human of humanSpeed: int
    | Computer of computerSpeed: int

// What a frame of play led to. A struct union keeps its case and fields in
// the value itself, so the 50 outcomes a second are never allocated (zero
// mode never frees memory). C# had to write this one out by hand.
[<Struct>]
type Outcome =
    | InPlay
    | HitWall
    | HitPaddle of paddle: Side
    | Scored of by: Side
// docs:end

// W and S, or the joystick in port 2: -1 for up, 1 for down, 0 for neither.
module Joystick =
    let direction () =
        let stick = C64.Joystick
        if C64.KeyHeld 'w' || (stick &&& C64.Up) <> 0 then -1
        elif C64.KeyHeld 's' || (stick &&& C64.Down) <> 0 then 1
        else 0

// One match: everything that changes as it is played.
type Match =
    { mutable Ball: Ball
      mutable LeftTop: int // the paddles' top edges
      mutable RightTop: int
      mutable Score: Score
      mutable Hits: int
      mutable Frames: int
      mutable Strays: int // frames with something off the field
      mutable Rally: int // hits since the last serve
      mutable Seed: uint32 }

    member m.PaddleTop side =
        match side with
        | Left -> m.LeftTop
        | Right -> m.RightTop

// docs:patterns
// Where the ball is, as the rules see it. Each is a partial active pattern
// that returns a voption, so matching on it allocates nothing.

// Above the top wall or below the bottom one.
[<return: Struct>]
let (|OffWall|_|) (ball: Ball) =
    if ball.Y < Court.Top || ball.Y > Court.Bottom - Court.BallSize then ValueSome() else ValueNone

// At a paddle's face, coming its way, and level with it.
[<return: Struct>]
let (|AtPaddle|_|) side (m: Match) =
    let ball = m.Ball
    let atFace =
        match side with
        | Left -> ball.Dx < 0 && ball.X <= Court.LeftFace && ball.X > Court.LeftFace - 8
        | Right ->
            ball.Dx > 0
            && ball.X >= Court.RightFace - Court.BallSize
            && ball.X < Court.RightFace - Court.BallSize + 8
    if atFace && ball.Meets(m.PaddleTop side) then ValueSome() else ValueNone

// Past a paddle and out: a point for the other side.
[<return: Struct>]
let (|PointFor|_|) (ball: Ball) =
    if ball.X < Court.Left then ValueSome Right
    elif ball.X > Court.Right - Court.BallSize then ValueSome Left
    else ValueNone
// docs:end

module Match =
    // A coin toss: the same tosses every match, so a test match always goes
    // the same way.
    let private heads (m: Match) =
        m.Seed <- m.Seed * 1103515245u + 12345u
        (m.Seed >>> 16) % 2u = 0u

    // From the middle, slowly, at an angle that varies.
    let serve towards (m: Match) =
        let dy = if heads m then 1 else 2
        m.Ball <-
            { X = Court.Middle - Court.BallSize / 2
              Y = Court.Center - Court.BallSize / 2
              Dx = (match towards with Right -> 2 | Left -> -2)
              Dy = if heads m then -dy else dy }
        m.Rally <- 0

    let create seed =
        let m =
            { Ball = { X = 0; Y = 0; Dx = 0; Dy = 0 }
              LeftTop = Court.Center - Court.PaddleHeight / 2
              RightTop = Court.Center - Court.PaddleHeight / 2
              Score = { Left = 0; Right = 0 }
              Hits = 0
              Frames = 0
              Strays = 0
              Rally = 0
              Seed = seed }
        serve (if heads m then Right else Left) m
        m

    let onField (m: Match) =
        m.Ball.Y >= Court.Top && m.Ball.Y <= Court.Bottom - Court.BallSize
        && m.LeftTop = Court.clamp m.LeftTop && m.RightTop = Court.clamp m.RightTop

    // The computer follows the ball once it is on its side and coming its
    // way; otherwise it goes back to the middle. It moves slower than a
    // steep ball, so it can be beaten.
    let private target side (m: Match) =
        let ball = m.Ball
        let coming =
            match side with
            | Left -> ball.Dx < 0 && ball.X < Court.Middle
            | Right -> ball.Dx > 0 && ball.X > Court.Middle
        if coming then ball.Middle else Court.Center

    // Where a paddle's top goes this frame.
    let private move top controller side (m: Match) =
        let wanted =
            match controller with
            | Human speed -> top + speed * Joystick.direction ()
            | Computer speed -> top + Math.Clamp(target side m - (top + Court.PaddleHeight / 2), -speed, speed)
        Court.clamp wanted

    // Off a paddle: the further from its middle the ball hits, the steeper it
    // leaves, and every fourth hit of a rally makes it faster.
    let private bounce side (m: Match) =
        let ball = m.Ball
        let dy =
            match (ball.Middle - (m.PaddleTop side + Court.PaddleHeight / 2)) / 6 with
            | 0 -> if heads m then -1 else 1
            | dy -> dy
        m.Rally <- m.Rally + 1
        m.Hits <- m.Hits + 1
        let dx =
            match -ball.Dx with
            | dx when m.Rally % 4 = 0 && abs dx < 4 -> dx + sign dx
            | dx -> dx
        let x =
            match side with
            | Left -> Court.LeftFace
            | Right -> Court.RightFace - Court.BallSize
        m.Ball <- { ball with X = x; Dx = dx; Dy = dy }
        HitPaddle side

    // docs:step
    let step left right (m: Match) =
        m.LeftTop <- move m.LeftTop left Left m
        m.RightTop <- move m.RightTop right Right m
        m.Ball <- { m.Ball with X = m.Ball.X + m.Ball.Dx; Y = m.Ball.Y + m.Ball.Dy }

        let wall =
            match m.Ball with
            | OffWall ->
                let ball = m.Ball
                m.Ball <- { ball with Y = Court.clampBall ball.Y; Dy = -ball.Dy }
                HitWall
            | _ -> InPlay
        let hit =
            match m with
            | AtPaddle Left -> bounce Left m
            | AtPaddle Right -> bounce Right m
            | _ -> wall
        match m.Ball with
        | PointFor side -> Scored side
        | _ -> hit
    // docs:end

// One of the SID's three voices. Each has 7 registers from $D400: the
// frequency, the pulse width, the control register (the waveform, and the
// gate that starts a note), and the envelope.
[<Struct>]
type Voice =
    { Number: int }

    static member Triangle = 0x10
    static member Sawtooth = 0x20
    static member Pulse = 0x40
    static member Noise = 0x80

    member private voice.Register offset = 0xD400 + voice.Number * 7 + offset

    // How loud a note is over time: it starts at full volume at once and
    // fades out over `decay` (0-15; 8 is about 300 ms). Sustain and release
    // are 0, so a note dies away by itself.
    member voice.Envelope decay =
        C64.Poke(voice.Register 5, decay)
        C64.Poke(voice.Register 6, 0)

    // Plays a note: frequency is the SID's own number (see Music.frequency).
    // Turning the gate off and on again starts the note from the top.
    member voice.Strike(frequency, waveform) =
        C64.Poke(voice.Register 0, frequency &&& 0xFF)
        C64.Poke(voice.Register 1, frequency >>> 8)
        C64.Poke(voice.Register 2, 0)
        C64.Poke(voice.Register 3, 8) // pulse width: half
        C64.Poke(voice.Register 4, waveform) // gate off
        C64.Poke(voice.Register 4, waveform ||| 1) // gate on

    member voice.Silence() = C64.Poke(voice.Register 4, 0)

// The sound effects, on the first voice: one for each thing a frame of play
// can lead to.
module Sound =
    let private effects = { Number = 0 }

    let on () = C64.Poke(0xD418, 15) // full volume
    let off () = C64.Poke(0xD418, 0)

    let private play frequency waveform decay =
        effects.Envelope decay
        effects.Strike(frequency, waveform)

    let after outcome =
        match outcome with
        | InPlay -> ()
        | HitWall -> play 12000 Voice.Triangle 7
        | HitPaddle _ -> play 20000 Voice.Sawtooth 8
        | Scored _ -> play 3000 Voice.Noise 10 // a burst of noise

// A tune of my own that loops: a melody on the second voice, a bass line on
// the third. tick plays the next step every 7 frames, so it costs nothing
// in between.
module Music =
    [<Literal>]
    let private FramesPerStep = 7

    // MIDI note numbers (60 is middle C), one per step; 0 lets the note
    // before it ring on. Four bars of eight steps: A minor, F, C, G.
    let private melody =
        [| 76uy; 0uy; 72uy; 76uy; 81uy; 0uy; 79uy; 76uy
           77uy; 0uy; 72uy; 77uy; 81uy; 0uy; 79uy; 77uy
           79uy; 0uy; 76uy; 72uy; 74uy; 76uy; 79uy; 0uy
           74uy; 0uy; 71uy; 74uy; 79uy; 77uy; 74uy; 71uy |]

    let private bass =
        [| 45uy; 0uy; 57uy; 0uy; 45uy; 0uy; 57uy; 0uy
           41uy; 0uy; 53uy; 0uy; 41uy; 0uy; 53uy; 0uy
           48uy; 0uy; 60uy; 0uy; 48uy; 0uy; 60uy; 0uy
           43uy; 0uy; 55uy; 0uy; 43uy; 0uy; 55uy; 0uy |]

    let private tune = { Number = 1 }
    let private low = { Number = 2 }
    let mutable private frame = 0
    let mutable private step = 0
    let mutable private playing = false

    // The SID's number for a note: the frequency in Hz times 17.03 on a
    // European (PAL) C64. The table is one high octave, MIDI 84-95; each
    // octave below is half the number.
    let private frequency note =
        let top =
            match note % 12 with
            | 0 -> 17820 | 1 -> 18880 | 2 -> 20003 | 3 -> 21192
            | 4 -> 22452 | 5 -> 23787 | 6 -> 25202 | 7 -> 26700
            | 8 -> 28288 | 9 -> 29970 | 10 -> 31752 | _ -> 33640
        top >>> (7 - note / 12)

    let private strike (voice: Voice) waveform note =
        if note <> 0uy then
            voice.Strike(frequency (int note), waveform)

    let start () =
        tune.Envelope 8
        low.Envelope 9
        frame <- 0
        step <- 0
        playing <- true

    let stop () =
        tune.Silence()
        low.Silence()
        playing <- false

    let toggle () = if playing then stop () else start ()

    let tick () =
        if playing then
            if frame % FramesPerStep = 0 then
                strike tune Voice.Pulse melody[step]
                strike low Voice.Sawtooth bass[step]
                step <- (step + 1) % melody.Length
            frame <- frame + 1

// The paddles and the ball are sprites; the net and the score are text.
module Screen =
    // Sprite numbers.
    [<Literal>]
    let private LeftPaddle = 0
    [<Literal>]
    let private RightPaddle = 1
    [<Literal>]
    let private Ball = 2

    // The sprites' shapes are made here (C# makes them on the stack).
    let private shape: byte[] = Array.zeroCreate 63

    let showScore (score: Score) =
        C64.Print(16, 1, score.Left, C64.LightBlue) |> ignore
        C64.Print(23, 1, score.Right, C64.LightRed) |> ignore

    let drawCourt score =
        C64.Clear(C64.Black)
        C64.Border(C64.DarkGray)
        // The net, on every other row: screen code 93, a vertical line.
        for row in 0..2 .. C64.Rows - 1 do
            let cell = row * C64.Columns + 20
            C64.Poke(0x0400 + cell, 93)
            C64.Poke(0xD800 + cell, C64.Gray)
        showScore score

        // A paddle is a bar 4 pixels wide, its 21 rows doubled; the ball is 4 by 4.
        for row in 0..20 do
            shape[row * 3] <- 0xF0uy
        C64.Sprite(LeftPaddle, 0, shape, C64.White)
        C64.Sprite(RightPaddle, 0, shape, C64.White)
        C64.Poke(0xD017, C64.Peek(0xD017) ||| 3) // sprites 0 and 1 twice as tall
        for i in Court.BallSize * 3 .. shape.Length - 1 do
            shape[i] <- 0uy
        C64.Sprite(Ball, 1, shape, C64.Yellow)

    let place (m: Match) =
        C64.MoveSprite(LeftPaddle, Court.LeftFace - 4, m.LeftTop)
        C64.MoveSprite(RightPaddle, Court.RightFace, m.RightTop)
        C64.MoveSprite(Ball, m.Ball.X, m.Ball.Y)

    let hide () =
        C64.HideSprite(LeftPaddle)
        C64.HideSprite(RightPaddle)
        C64.HideSprite(Ball)

    // Back to the colors BASIC starts with.
    let leave () =
        hide ()
        C64.Clear(C64.Blue)
        C64.Border(C64.LightBlue)

    // Waits for the next frame, and plays the music's next step.
    let nextFrame () =
        C64.WaitFrame()
        Music.tick ()

    let wait frames =
        for _ in 1..frames do
            nextFrame ()

    let print column row (text: string) color =
        C64.Print(column, row, text, color) |> ignore

// The key pressed since the last frame, if any. M turns the music on or
// off wherever the game is.
let private readKey () =
    match C64.ReadKey() with
    | 0 -> ValueNone
    | key ->
        if key = int 'm' then
            Music.toggle ()
        ValueSome(char key)

// docs:play
// What one frame of play led to: the match goes on, `pause` frames before
// the ball moves, or it is over, won or stopped by a key.
[<Struct>]
type private Next =
    | Continue of pause: int
    | Over of won: bool

let private frame pause (m: Match) left right demo =
    Screen.nextFrame ()
    Screen.place m
    match readKey () with
    | ValueSome 'q' -> Over false
    | ValueSome key when demo && key <> 'm' -> Over false // any other key ends a demo
    | _ when pause > 0 -> Continue(pause - 1)
    | _ ->
        let outcome = Match.step left right m
        if not (Match.onField m) then
            m.Strays <- m.Strays + 1
        Sound.after outcome
        match outcome with
        | Scored side ->
            m.Score <-
                match side with
                | Left -> { m.Score with Left = m.Score.Left + 1 }
                | Right -> { m.Score with Right = m.Score.Right + 1 }
            Screen.showScore m.Score
            if m.Score.Winner.IsSome then
                Over true
            else
                Match.serve side.Opposite m
                Continue 50 // a second before the next serve
        | _ -> Continue 0

// Frame after frame until the match is over. The call to itself is a jump:
// F# makes the recursion a loop. (A function inside play would be a closure,
// an object, made while the test measures what playing allocates.)
let rec private playOn pause (m: Match) left right demo maxFrames =
    if m.Frames >= maxFrames then
        true
    else
        match frame pause m left right demo with
        | Over won -> won
        | Continue pause ->
            m.Frames <- m.Frames + 1
            playOn pause m left right demo maxFrames

// Plays the match until someone wins (true) or a key stops it (false), with
// a second before the first serve.
let play (m: Match) left right demo maxFrames =
    Screen.drawCourt m.Score
    playOn 50 m left right demo maxFrames
// docs:end

[<Struct>]
type Mode =
    | Play
    | Demo
    | Test
    | Quit

// A key that chooses a mode, frame after frame; Demo after ten seconds.
let rec private chooseMode frame =
    if frame = 500 then
        Demo
    else
        Screen.nextFrame ()
        match readKey () with
        | ValueSome ' ' -> Play
        | ValueSome 't' -> Test
        | ValueSome 'q' -> Quit
        | _ -> chooseMode (frame + 1)

// The title screen. (A loop, not String.iteri with a lambda: on a C64 the
// closure classes would cost 2 KB.)
let title () =
    Screen.hide ()
    C64.Clear(C64.Black)
    C64.Border(C64.DarkGray)
    let name = "P A D D L E S"
    for i in 0 .. name.Length - 1 do
        C64.Print(13 + i, 4, name[i], C64.Cyan + i % 6)
    Screen.print 6 8 "You are the left paddle:" C64.LightGray
    Screen.print 6 10 "W and S, or a joystick in port 2." C64.LightGray
    Screen.print 6 12 "The first to 7 points wins." C64.LightGray
    Screen.print 6 16 "SPACE  play" C64.White
    Screen.print 6 18 "M      music on or off" C64.White
    Screen.print 6 20 "Q      back to BASIC" C64.White
    Screen.print 3 23 "Or wait, and the C64 plays itself." C64.Gray
    chooseMode 0

let gameOver winner demo =
    let text =
        match winner, demo with
        | Left, false -> "You win!"
        | Right, false -> "The C64 wins"
        | Left, true -> "Left wins"
        | Right, true -> "Right wins"
    Screen.print (20 - text.Length / 2) 12 text C64.Yellow
    Screen.wait 150

// 0 if a check passed; 1, and the check's name, if it failed.
let private check (name: string) ok =
    if ok then
        0
    else
        Out.Write("  failed: ")
        Out.Line(name)
        1

// One match, computer against computer, then what happened in it: as text,
// after the screen is cleared. Returns the number of failed checks.
let test computer =
    let maxFrames = 25000 // 8 minutes; a match takes a few
    let m = Match.create 1982u
    let before = GC.GetTotalAllocatedBytes()
    play m computer computer false maxFrames |> ignore
    let allocated = GC.GetTotalAllocatedBytes() - before
    Screen.leave ()
    // Out.Write, piece by piece: written with `string` and +, and the checks
    // as an array filtered with a lambda, the game was 9 KB bigger.
    Out.Write("Test match: ")
    Out.Write(int64 m.Score.Left)
    Out.Write(" to ")
    Out.Write(int64 m.Score.Right)
    Out.Write(", ")
    Out.Write(int64 m.Hits)
    Out.Write(" hits in ")
    Out.Write(int64 m.Frames)
    Out.Line(" frames")

    let failures =
        check "someone won" m.Score.Winner.IsSome
        + check "it ended in time" (m.Frames < maxFrames)
        + check "the paddles hit the ball" (m.Hits > 2 * (m.Score.Left + m.Score.Right))
        + check "everything stayed on the field" (m.Strays = 0)
        + check "nothing was allocated while playing" (allocated = 0L)
    if failures = 0 then
        Out.Line("All 5 checks passed.")
    failures

[<EntryPoint>]
let main _ =
    let you, computer = Human 3, Computer 2
    Sound.on ()
    Music.start ()
    let rec run mode =
        match mode with
        | Quit -> 0
        | Test -> test computer
        | Play
        | Demo ->
            let demo = mode.IsDemo
            let m = Match.create 1982u
            if play m (if demo then computer else you) computer demo Int32.MaxValue then
                m.Score.Winner |> ValueOption.iter (fun winner -> gameOver winner demo)
            run (title ())
    let result = run (title ())
    Music.stop ()
    Sound.off ()
    if result = 0 then
        Screen.leave ()
    result
