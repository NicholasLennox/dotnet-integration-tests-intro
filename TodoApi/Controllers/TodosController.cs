using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Controllers
{
    [ApiController]
    [Route("api/todos")]
    public class TodosController : ControllerBase
    {
        private readonly TodoDbContext _db;

        public TodosController(TodoDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _db.Todos.ToListAsync());

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var todo = await _db.Todos.FindAsync(id);
            return todo is null ? NotFound() : Ok(todo);
        }

        [HttpPost]
        public async Task<IActionResult> Create(Todo todo)
        {
            _db.Todos.Add(todo);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = todo.Id }, todo);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, Todo todo)
        {
            if (id != todo.Id) return BadRequest();
            _db.Entry(todo).State = EntityState.Modified;
            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var todo = await _db.Todos.FindAsync(id);
            if (todo is null) return NotFound();
            _db.Todos.Remove(todo);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
