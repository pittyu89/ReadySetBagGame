"""Builds Docs/ReadySetBag_Project_Submission.pdf from Docs/screenshots.

Run from anywhere:  python Docs/make_pdf.py
"""
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (Image, KeepTogether, PageBreak, Paragraph,
                                SimpleDocTemplate, Spacer, Table, TableStyle)

HERE = Path(__file__).resolve().parent
SHOTS = HERE / "screenshots"
OUT = HERE / "ReadySetBag_Project_Submission.pdf"

ORANGE = colors.HexColor("#E8793A")
DARK = colors.HexColor("#2E2E2E")
LIGHT = colors.HexColor("#FBEDE3")

ss = getSampleStyleSheet()
H1 = ParagraphStyle("H1", parent=ss["Heading1"], fontName="Helvetica-Bold", fontSize=17,
                    textColor=ORANGE, spaceBefore=4, spaceAfter=8)
H2 = ParagraphStyle("H2", parent=ss["Heading2"], fontName="Helvetica-Bold", fontSize=12.5,
                    textColor=DARK, spaceBefore=8, spaceAfter=4)
BODY = ParagraphStyle("Body", parent=ss["BodyText"], fontName="Helvetica", fontSize=10.5,
                      leading=15, alignment=TA_JUSTIFY, textColor=DARK)
BUL = ParagraphStyle("Bul", parent=BODY, alignment=0, leftIndent=12, bulletIndent=2,
                     spaceAfter=2)
CELL = ParagraphStyle("Cell", parent=BODY, fontSize=9.5, leading=12.5, alignment=0)
CELLB = ParagraphStyle("CellB", parent=CELL, fontName="Helvetica-Bold")
CAP_T = ParagraphStyle("CapT", parent=BODY, fontName="Helvetica-Bold", fontSize=11,
                       spaceBefore=4, spaceAfter=2, alignment=0)
CAP = ParagraphStyle("Cap", parent=BODY, fontSize=9.8, leading=13.5)
TITLE = ParagraphStyle("Title", parent=BODY, fontName="Helvetica-Bold", fontSize=30,
                       leading=36, alignment=TA_CENTER, textColor=ORANGE)
SUB = ParagraphStyle("Sub", parent=BODY, fontSize=13, leading=18, alignment=TA_CENTER)

CONTENT_W = A4[0] - 40 * mm


def shot(name, width=CONTENT_W):
    from reportlab.lib.utils import ImageReader
    iw, ih = ImageReader(str(SHOTS / name)).getSize()
    return Image(str(SHOTS / name), width=width, height=width * ih / iw)


def table(rows, col_widths):
    data = [[Paragraph(c, CELLB if r == 0 or i == 0 else CELL) for i, c in enumerate(row)]
            for r, row in enumerate(rows)]
    t = Table(data, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), ORANGE),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
        ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#D9C4B5")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]))
    # Header row text must be white on orange
    for i in range(len(rows[0])):
        data[0][i].style = ParagraphStyle("hdr", parent=CELLB, textColor=colors.white)
    return t


def footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Helvetica", 8.5)
    canvas.setFillColor(colors.grey)
    canvas.drawString(20 * mm, 12 * mm, "ReadySetBag! — Project Documentation")
    canvas.drawRightString(A4[0] - 20 * mm, 12 * mm, f"Page {doc.page}")
    canvas.restoreState()


ABSTRACT = (
    "<b>ReadySetBag!</b> is a 3D mobile serious game for Android that teaches students how to "
    "prepare an emergency go-bag. Racing against a countdown, the player explores a house, "
    "searches drawers, cabinets and shelves for supplies, and packs a backpack with a strict "
    "5 kg weight limit, choosing life-saving essentials over tempting but useless items. Once "
    "the bag is packed, a teacher character presents disaster scenarios in Filipino, such as an "
    "earthquake blackout or being trapped behind a collapsed wall, and the player answers by "
    "dragging the correct item out of the bag. Each correct answer opens a short hands-on "
    "minigame that shows how the item is actually used. A 100-point score rates packing, quiz, "
    "task and time performance. Teachers can run live class sessions through a join code with "
    "results saved to the cloud, while offline practice lets students replay and unlock harder "
    "levels."
)

FEATURES = [
    ("Explore-and-pack gameplay",
     "Move through a fully modelled 3D house with an on-screen joystick and camera controls. "
     "Supplies are hidden in furniture; tapping a drawer or cabinet opens it so its contents "
     "can be dragged into the go-bag."),
    ("Grid-based go-bag inventory",
     "Items take up real space on a grid and have real weights. Three bag types (small, medium "
     "and standard backpack) each have their own pockets, and the bag cannot exceed 5 kg."),
    ("41 supply items in five importance tiers",
     "Critical (e.g. water bottle, first aid kit, flashlight, whistle), Important, Useful, "
     "Conditional and Nuisance items (e.g. game console, pillow, soy sauce) teach students to "
     "tell essentials from clutter."),
    ("Scenario quiz answered with the bag",
     "A bank of 20 disaster-scenario questions written in Filipino. Players answer by dragging "
     "an item from their own bag, so an essential left behind cannot be used to answer. "
     "Each answer is followed by an explanation."),
    ("20 hands-on minigames",
     "One for every quiz question. Each correct answer opens a quick activity showing how the "
     "item is used: shining a flashlight or glow stick to find people, blowing a whistle for "
     "rescuers, dressing a wound, pouring water, tuning a radio, loading batteries, tying a "
     "rope knot, and more."),
    ("Three difficulty levels with unlocks",
     "Beginner (10 min, 10 questions), Intermediate (8 min, 15 questions, garage opens) and "
     "Advanced (6 min, 20 questions, second floor opens). Scoring 70+ unlocks Intermediate "
     "and 85+ unlocks Advanced."),
    ("100-point drill score and badges",
     "Score = 40% packing + 20% quiz + 20% minigame completion + 20% time bonus. Results show a "
     "breakdown and a learning-stage badge: Cognitive, Associative or Autonomous."),
    ("Teacher sessions and student accounts",
     "Students log in with Firebase accounts and join a teacher's live session with a code. "
     "The teacher sets the difficulty, and each student's result is uploaded to Cloud "
     "Firestore for the teacher/admin dashboards (saved on the device and re-sent if offline)."),
    ("Offline practice mode",
     "Students (or guests) can play anytime without internet; scores, unlocked levels and the "
     "Journal are saved on the device."),
    ("Item Journal",
     "Every item a player has packed is unlocked in a Journal that shows its weight, importance "
     "and a short description, building a personal reference of go-bag supplies."),
    ("Replayability",
     "Player and go-bag spawn points are randomized each run and questions are drawn from the "
     "bank in random order, so every drill is different."),
    ("Polish and accessibility",
     "Selectable characters, pixel-art UI, guided tutorial, music and sound effects with "
     "volume settings, haptic feedback, pause menu, and resume support if the game is closed "
     "mid-drill."),
]

SCREENS = [
    ("01_title.png", "Title Screen",
     "The game's pixel-art logo. Tapping anywhere continues to the login screen."),
    ("02_login.png", "Student Log In",
     "Students sign in with the account their teacher created. A toggle switches to guest "
     "play, and the Terms and Conditions must be accepted before playing."),
    ("03_menu.png", "Main Menu",
     "Play, Options, Switch (character select), Exit, How to Play and About, over an animated "
     "bedroom scene. The ID card shows the logged-in player."),
    ("04_mode.png", "Game Mode Selection",
     "Teacher Session joins a live class session by code; Practice Session lets the student "
     "play offline as many times as they like."),
    ("05_difficulty.png", "Difficulty and Go-Bag Selection",
     "Choose Beginner, Intermediate or Advanced (each with a map preview and rules) and pick "
     "which backpack to carry."),
    ("06_game.png", "Exploring the House",
     "The player moves with the virtual joystick. The countdown timer is shown at the top; "
     "the house includes details such as the family cat wandering around."),
    ("08_storage.png", "Searching Storage and Packing",
     "Opening furniture shows its compartments (right) next to the go-bag grid (left). Items "
     "are dragged into the bag's pockets; the bag icon shows how full it is by weight."),
    ("09_journal.png", "Item Journal",
     "Unlocked items with their weight, importance level and description. Locked slots are "
     "items the player has not packed yet."),
    ("10_quiz.png", "Scenario Quiz",
     "The teacher character presents an emergency scenario (here: a blackout after an "
     "earthquake). The player drags the correct item from the bag into the answer box."),
    ("11_feedback.png", "Answer Feedback",
     "After answering, the teacher explains why the item is right (here: a whistle carries "
     "farther than a voice and does not tire you out while waiting for rescue)."),
    ("12_minigame.png", "Minigame: Whistle",
     "A correct answer opens a timed minigame. Here the player taps repeatedly until the "
     "rescuers notice them."),
    ("13_firstaid.png", "Minigame: First Aid",
     "The player checks the arm for wounds, cleans the bruises and covers the wound with gauze "
     "using tools from the first aid kit."),
    ("14_results.png", "Results",
     "The final drill score with a breakdown of packing, quiz, minigame and time bonus, plus "
     "the learning-stage badge. Shown from a sample test run; from here the player can retry, "
     "move to the next difficulty or return to the menu."),
]


def build():
    doc = SimpleDocTemplate(str(OUT), pagesize=A4, leftMargin=20 * mm, rightMargin=20 * mm,
                            topMargin=18 * mm, bottomMargin=20 * mm,
                            title="ReadySetBag! Project Documentation", author="ReadySetBag Team")
    s = []

    # Cover + abstract
    s += [Spacer(1, 6 * mm), Paragraph("ReadySetBag!", TITLE),
          Paragraph("An Emergency Go-Bag Preparedness Game for Android", SUB),
          Spacer(1, 6 * mm), shot("01_title.png", CONTENT_W * 0.8), Spacer(1, 8 * mm),
          Paragraph("1. App Concept Description (Abstract)", H1),
          Paragraph(ABSTRACT, BODY), PageBreak()]

    # Requirements
    s.append(Paragraph("2. Hardware and Software Requirements", H1))
    s.append(Paragraph("2.1 End-User Device (to play the game)", H2))
    s.append(table([
        ["Component", "Minimum", "Recommended"],
        ["Operating system", "Android 7.1 (API level 25)", "Android 10 or newer"],
        ["Processor", "64-bit ARM (arm64-v8a) processor", "Octa-core 64-bit ARM, 2.0 GHz+"],
        ["Graphics", "OpenGL ES 3.0 or Vulkan capable GPU", "Vulkan capable GPU"],
        ["Memory (RAM)", "3 GB", "4 GB or more"],
        ["Storage", "About 250 MB free (installer is about 67 MB)", "500 MB free"],
        ["Display", "Touchscreen, landscape orientation", "6 inch or larger, 1080p"],
        ["Network", "None for Practice mode", "Internet (Wi-Fi or mobile data) for student "
                                                "log in and Teacher Sessions"],
        ["Other", "Speakers or headphones; vibration motor (optional)", ""],
    ], [32 * mm, 66 * mm, CONTENT_W - 98 * mm]))

    s.append(Paragraph("2.2 Development Environment", H2))
    s.append(table([
        ["Item", "Specification"],
        ["Development PC", "Windows 10/11 64-bit, quad-core CPU, 16 GB RAM, DirectX 11/12 "
                           "GPU, 30 GB free disk space"],
        ["Game engine", "Unity 6 (6000.3.24f1) with the Universal Render Pipeline (URP)"],
        ["Programming language", "C# (.NET), IL2CPP scripting backend"],
        ["IDE", "Microsoft Visual Studio"],
        ["Build target", "Android SDK and NDK via Unity Android Build Support; "
                         "OpenJDK; Android Studio emulator / physical device for testing"],
        ["Unity packages", "Universal RP, Cinemachine, TextMeshPro, uGUI, Video Player, "
                           "2D Sprite, Unity Test Framework"],
        ["Backend services", "Google Firebase: Authentication (student accounts) and Cloud "
                             "Firestore (sessions and results)"],
        ["Art and assets", "Aseprite (pixel-art sprites and UI), 3D house models (FBX), "
                           "Jersey 25 font"],
        ["Version control", "Git"],
    ], [38 * mm, CONTENT_W - 38 * mm]))
    s.append(PageBreak())

    # Features
    s.append(Paragraph("3. Product Features", H1))
    for title, text in FEATURES:
        s.append(Paragraph(f"<b>{title}.</b> {text}", BUL, bulletText="•"))
    s.append(PageBreak())

    # Screenshots, two per page
    s.append(Paragraph("4. Screenshots and Descriptions", H1))
    w = CONTENT_W * 0.92
    for i, (f, t, d) in enumerate(SCREENS, start=1):
        framed = Table([[shot(f, w)]], colWidths=[w])
        framed.setStyle(TableStyle([("BOX", (0, 0), (-1, -1), 0.8, colors.HexColor("#BBBBBB")),
                                    ("LEFTPADDING", (0, 0), (-1, -1), 0),
                                    ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                                    ("TOPPADDING", (0, 0), (-1, -1), 0),
                                    ("BOTTOMPADDING", (0, 0), (-1, -1), 0)]))
        s.append(KeepTogether([framed, Paragraph(f"Figure {i}. {t}", CAP_T),
                               Paragraph(d, CAP), Spacer(1, 7 * mm)]))

    doc.build(s, onFirstPage=footer, onLaterPages=footer)
    print("words in abstract:", len(Paragraph(ABSTRACT, BODY).getPlainText().split()))
    print(OUT)


if __name__ == "__main__":
    build()
