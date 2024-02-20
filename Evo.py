import pygame
import random
import math

pygame.init()

screen = pygame.display.set_mode((800, 600))
pygame.display.set_caption("Evolution Simulation")

WHITE = (255, 255, 255)
BLACK = (0, 0, 0)

clock = pygame.time.Clock()

squares = []
last_square_time = pygame.time.get_ticks()
last_background_time = pygame.time.get_ticks()
background_color = WHITE
generation_time = 5000  # in milliseconds
generation_chance = 0.5  # in percentage
simulation_speed = 100  # in multiples of real time

class Square:
    def __init__(self, x, y, color, width=50, height=50):
        self.x = x
        self.y = y
        self.color = color
        self.width = width
        self.height = height
        self.time_created = pygame.time.get_ticks()
        self.last_checked = pygame.time.get_ticks()

    def draw(self):
        pygame.draw.rect(screen, self.color, (self.x, self.y, self.width, self.height))

    def check_delete(self, background_color):
        if pygame.time.get_ticks() - self.last_checked >= random.randint(1000, 3000):
            self.last_checked = pygame.time.get_ticks()
            if not self.color_similar(background_color, self.color) and random.random() <= 0.90:
                return True
        return False

    def color_similar(self, color1, color2, threshold=100):
        return all(abs(c1 - c2) <= threshold for (c1, c2) in zip(color1, color2))

    def multiply(self):
        if pygame.time.get_ticks() - self.time_created >= 10000:
            # Calculate a random distance and angle from the original square's position
            distance = random.randint(20, 80)
            angle = random.uniform(0, 2 * math.pi)

            # Calculate the x and y values of the new square's position
            new_x = int(self.x + distance * math.cos(angle))
            new_y = int(self.y + distance * math.sin(angle))

            # Create and return the new square with slightly different color
            r, g, b = self.color
            r = r + random.randint(-2, 2)
            g = g + random.randint(-2, 2)
            b = b + random.randint(-2, 2)
            r = min(max(r, 0), 255)
            g = min(max(g, 0), 255)
            b = min(max(b, 0), 255)
            return Square(new_x, new_y, (r, g, b))
        return None

    def die(self):
        self.time_created = pygame.time.get_ticks() - 10000

    def check_delete(self, background_color):
        if pygame.time.get_ticks() - self.last_checked >= random.randint(1000, 3000):
            self.last_checked = pygame.time.get_ticks()
            color_diff = sum(abs(c1 - c2) for c1, c2 in zip(self.color, background_color))
            prob_delete = 0.2 + color_diff / 1000.0
            if random.random() <= prob_delete:
                return True
        return False


# Define functions
def kill_squares(squares):
    if len(squares) > 10000:
        num_to_delete = len(squares) - 10000
        for i in range(num_to_delete):
            random_square = random.choice(squares)
            squares.remove(random_square)


def create_square():
    x = random.randint(0, 1000)
    y = random.randint(0, 2000)
    color = (random.randint(0, 255), random.randint(0, 255), random.randint(0, 255))
    return Square(x, y, color)


# Game loop
running = True
while running:
    for event in pygame.event.get():
        if event.type == pygame.QUIT:
            running = False
        elif event.type == pygame.KEYDOWN:
            if event.key == pygame.K_SPACE:  # Check if the spacebar is pressed
                # Change the background color to a random color
                background_color = (random.randint(0, 255), random.randint(0, 255), random.randint(0, 255))
                last_background_time = pygame.time.get_ticks()

    # Generate new squares
    if pygame.time.get_ticks() - last_square_time >= generation_time // simulation_speed:
        last_square_time = pygame.time.get_ticks()
        if random.random() <= generation_chance:
            squares.append(create_square())

    # Delete old squares and multiply existing ones
    for square in squares:
        if square.check_delete(background_color):
            squares.remove(square)
        new_square = square.multiply()
        if new_square is not None:
            squares.append(new_square)

    # Kill squares if their total number exceeds 1000
    kill_squares(squares)

    # Draw the squares and background
    screen.fill(background_color)
    for square in squares:
        square.draw()

    pygame.display.update()

    clock.tick(60)

