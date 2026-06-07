# GoalFueled App

## Overview

GoalFueled is an app that allows users to define and track plans to help them achieve health/life goals.

## Architecture

This will be a serverless web app that will be used primarily on mobile but should also look appropriate on larger devices.

**Technology**  

- Will be hosted on `Microsoft Azure`.
- Will be deployed to `Microsoft Azure DevOps` using YAML pipelines.
- Code repo will exist on `GitHub.com` and will use GitHub actions for DevOps.
- `TypeScript React` (latest version) will be used for the frontend.
- `VITE` (latest version) will be used to host `React`.
- `Azure Cosmos DB` will be used for the backend.
- `Entity Framework` will be used for communication with and deployment of `Azure Cosmos DB` instance.
- `.NET Core v8.0` and `C#` (latest version) will be the preferred languages for API & Function code.
- APIs will exist as `Azure Functions`.

## MVP App

The initial functionality for the MVP app will be centered around defining ingredients, combining ingredients into recipes, then defining meal plans based on those recipes that target a user's specific goal. Goals could include weight loss, weight management, or weight gain.

**Features**  

1. User Authentication

    - Allow authentication using Google, Microsoft, and Facebook
    - After initial authentication, a user profile should be created and persisted for the user.
    - Upon first login, user should be presented with a welcome page that asks them what their first goal is.

1. User Authorization

    - Initially, there will only be two roles:
        - `User` role: regular user that can only access their data
        - `App Admin` role: an admin of the app that can view and modify all data. An admin can promote other users to be `App Admin` users. The very first user to join the app should automatically be given the `App Admin` role. An `App Admin` should be able to turn on/off their view of admin features so that they can use the app like a regular user when they want to.

1. Ingredients

    - An `ingredient` is the most basic components of a `recipe`.
    - An `ingredient` should have these basic attributes intially.
        - `Name`: common name of the ingredient.
        - `Description`: description of the ingredient. May contain HTML formatted text.
        - `Image`: uploadable image of the ingredient
        - `UnitOfMeasure`: the units used to define a single serving of this ingredient.
        - `NutrionalInfoPerUnit`: a collection of nutritional attributes such as `Calories`, `Vitamin C`, etc. Research nutritional label data and build a collection of available attributes. Each attribute should be able to have a value assigned to it.
    - An `ingredient` can be defined at the app level by an `app admin` or a user can add their own `ingredients`.

1. Recipes

    - A `recipe` is a collection of `ingredients` that can be combined to make a `meal`.
    - A `recipe` can be defined at the app level by an `app admin` or a user can add their own `ingredients`.
    - If a user creates a custom `recipe` they should be able to submit it as a public recipe. If a recipe is submitted to be made public, it has to be reviewed by an admin before it can be made visible to the public. Once a recipe is made public, it cannot be made private again. The original user will be credited with it's creation, however.
    - An image of a prepared `recipe` should be allowed.
    - `Nutritional Info` should be shown for every recipe and it should be a summary of the total nutritional info of the `ingredients` in the `recipe`.

1. Meals

    - A `meal` is a collection of portions of prepared `recipes`.

1. Meal Plans

    - A `meal plan` is a plan for when to consume daily `meals`.
    - A user should be able to define a daily or weekly `meal plan` consisting of which `meals` will be consumed each day and at what time.
    - A `meal plan` may also contain `meals` or `ingredients` (ex. water) that should be consumed during the day that do not have a specific time when they should be consumed.
    - A user should be able to see a nutritional summary of their daily consumption.
